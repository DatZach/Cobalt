using System.Buffers;
using System.Diagnostics;
using System.Text;

namespace Compiler.CodeGeneration.Artifacts
{
    [DebuggerDisplay("Variable {Name}: {Type} = {Value}")]
    internal record Variable : ISymbol
    {
        public string Name { get; }

        public CobType Type { get; }

        public bool Mutable { get; }

        public object? Value { get; set; }

        public Variable[] StructValue
        {
            get => Value as Variable[] ?? throw new InvalidDataException();
            set => Value = value;
        }

        public byte[] BufferValue
        {
            get => Value as byte[] ?? throw new InvalidDataException();
            set => Value = value;
        }

        public long IntValue
        {
            get => Value switch
            {
                byte[] x => GetPinnedAddress(x),
                long x => x,
                _ => throw new InvalidDataException()
            };

            set => Value = value;
        }

        public Variable(string name, CobType type, bool mutable, object? value)
        {
            Name = name;
            Type = type;
            Mutable = mutable;
            Value = value;
        }

        public Variable(string name, CobType type, bool mutable, long value)
            : this(name, type, mutable, (object)value)
        {

        }

        public Variable(string name, CobType type, bool mutable)
            : this(name, type, mutable, null)
        {

        }

        public Variable(string name, CobType type, object? value)
            : this(name, type, true, value)
        {

        }

        public Variable(string name, CobType type, long value)
            : this(name, type, true, (object)value)
        {

        }

        public Variable(string name, CobType type)
            : this(name, type, true, null)
        {

        }

        public virtual bool IsVisibleTo(IScopeContext context) => true;

        public Variable DeepClone()
        {
            if (Type == eCobType.Struct) // Structs are reference types
                return this;

            object? value;
            if (Value is Variable[] srcStructValue) // Tuples are value types
            {
                var dstStructValue = new Variable[srcStructValue.Length];
                for (int i = 0; i < srcStructValue.Length; ++i)
                    dstStructValue[i] = srcStructValue[i].DeepClone();
                value = dstStructValue;
            }
            else if (Value is byte[] srcBufferValue)
            {
                var dstBufferValue = new byte[srcBufferValue.Length];
                srcBufferValue.CopyTo(dstBufferValue, 0);
                value = dstBufferValue;
            }
            else if (Value is long srcLongValue)
                value = srcLongValue;
            else if (Value == null)
                value = null;
            else
                throw new NotImplementedException();

            return this with { Value = value };
        }

        public Variable? ElementAt(long idx)
        {
            if (Value is byte[] bufferValue)
                return new Variable("$elem", CobType.U8, false, bufferValue[idx]);
            if (Value is string stringValue)
                return new Variable("$elem", CobType.U8, false, stringValue[(int)idx]);

            return null; // TODO Exception?
        }

        public override string ToString()
        {
            string value;
            if (Type == CobType.String)
                value = '"' + Encoding.UTF8.GetString(BufferValue).Replace("\n", "^n") + '"';
            else
                value = Value?.ToString() ?? "(null)";

            return $"{Name,-25}{Type} = {value}";
        }

        private MemoryHandle? pinnedAddress;
        private unsafe nint GetPinnedAddress(byte[] buffer)
        {
            if (pinnedAddress == null)
            {
                var data = new Memory<byte>(buffer);
                pinnedAddress = data.Pin();
            }

            return (nint)pinnedAddress.Value.Pointer;
        }

        ~Variable()
        {
            if (pinnedAddress != null)
            {
                pinnedAddress.Value.Dispose();
                pinnedAddress = null;
            }
        }
    }

    internal sealed record CobType
    {
        public readonly static CobType None = eCobType.None;
        public readonly static CobType Any = eCobType.Any;
        public readonly static CobType Func = eCobType.Function;
        public readonly static CobType Boolean = new(eCobType.Boolean);
        public readonly static CobType Int = new (eCobType.Signed, -1);
        public readonly static CobType UInt = new (eCobType.Unsigned, -1);
        public readonly static CobType Float = new (eCobType.Float, -1);
        public readonly static CobType U8 = new (eCobType.Unsigned, 8);
        public readonly static CobType U32 = new(eCobType.Unsigned, 32);
        public readonly static CobType U64 = new(eCobType.Unsigned, 64);
        public readonly static CobType Char = new(eCobType.Unsigned, 8);// { AliasName = "char" };
        public readonly static CobType String = new (eCobType.Array, elementType: Char, tag: StringContext.Instance) { AliasName = "string" }; // TODO Remove
        public readonly static CobType Module = eCobType.Module;
        public readonly static CobType Error = eCobType.Error;
        public readonly static CobType Nil = eCobType.Nil;
        public readonly static CobType Generic = eCobType.Generic;

        public string? AliasName { get; init; } // TODO Remove?

        public eCobType Type { get; }

        public int Size { get; }

        public bool HasErrorFlag => Type == eCobType.Union && UnionedTypes!.Contains(Error);

        public bool HasNilFlag => Type == eCobType.Union && UnionedTypes!.Contains(Nil);

        public CobType? ElementType { get; }

        public IReadOnlyList<CobType>? UnionedTypes { get; }

        public object? Tag { get; }

        public Function? TagFunction => Tag as Function;

        public CobType(
            eCobType type,
            int size = -1,
            CobType? elementType = null,
            IReadOnlyList<CobType>? unionedTypes = null,
            object? tag = null
        ) {
            Type = type;
            Size = size;
            ElementType = elementType;
            UnionedTypes = unionedTypes;
            Tag = tag;
        }

        public static implicit operator CobType(eCobType type)
        {
            return new CobType(type, -1);
        }
        
        public override int GetHashCode()
        {
            return HashCode.Combine((int)Type, Size, ElementType, Tag);
        }

        public static bool operator ==(CobType left, eCobType right)
        {
            return EqualsType(left, right);
        }

        public static bool operator !=(CobType left, eCobType right)
        {
            return !EqualsType(left, right);
        }

        private static bool EqualsType(CobType left, eCobType right)
        {
            if (left == null)
                return false;

            return left.Type == right;
        }

        public static CobType FromString(string? typeName, IScopeContext? context = null)
        {
            if (string.IsNullOrEmpty(typeName))
                return None;

            // UNION TYPE
            var isUnion = typeName.Contains('|');
            if (isUnion)
            {
                var subTypes = typeName.Split('|', StringSplitOptions.TrimEntries);
                return new CobType(
                    eCobType.Union,
                    unionedTypes: subTypes.Select(x => FromString(x, context)).ToList()
                );
            }


            // SIMPLE TYPE
            CobType type;
            CobType bType = null;

            var isError = typeName.EndsWith('!');
            if (isError)
                typeName = typeName[..^1];

            var isNil = typeName.EndsWith('?');
            if (isNil)
                typeName = typeName[..^1];

            var genericIdx = typeName.IndexOf('`');
            if (genericIdx != -1)
            {
                bType = FromString(typeName[(genericIdx+1)..], context);
                typeName = typeName[..genericIdx];
            }

            var isArray = typeName.EndsWith("[]");
            
            if (isArray)
            {
                var elementType = FromString(typeName[..^2], context);
                //type = new CobType(eCobType.Array, elementType: elementType);
                var tag = Intrinsics.Array.FindConcretizedStruct(elementType) ?? Intrinsics.Array.AllocateConcretizedStruct(elementType);


                return new CobType(eCobType.Struct, tag: tag);
            }
            else if (typeName.Length >= 2 && typeName[0] == 's' && char.IsDigit(typeName[1]))
                type = new CobType(eCobType.Signed, int.Parse(typeName[1..]));
            else if (typeName.Length >= 2 && typeName[0] == 'u' && char.IsDigit(typeName[1]))
                type = new CobType(eCobType.Unsigned, int.Parse(typeName[1..]));
            else if (typeName.Length >= 2 &&  typeName[0] == 'f' && char.IsDigit(typeName[1]))
                type = new CobType(eCobType.Float, int.Parse(typeName[1..]));
            else if (Aliases.TryGetValue(typeName, out var aliasType))
                type = aliasType;
            else if (context is StructType structType && (aliasType = structType.FindGenericType(typeName)) != null)
                type = aliasType;
            else if (context is TupleType tupleType && (aliasType = tupleType.FindGenericType(typeName)) != null)
                type = aliasType;
            else
                throw new Exception($"The typename '{typeName}' is not valid");

            if (bType != null)
            {
                if (type.Tag is TupleType tupleType)
                {
                    // TODO Might be better to just merge these methods into a single one
                    var tag = tupleType.FindConcretizedTuple(bType) ?? tupleType.AllocateConcretizedTuple(bType);
                    type = new CobType(eCobType.Tuple, tag: tag);
                } 
                else if (type.Tag is StructType structType)
                {
                    var tag = structType.FindConcretizedStruct(bType) ?? structType.AllocateConcretizedStruct(bType);
                    type = new CobType(eCobType.Struct, tag: tag);
                }
                else
                    throw new Exception($"The typename '{typeName}' is not valid");
            }

            if (isError && isNil)
                return new CobType(eCobType.Union, unionedTypes: new[] { type, Error, Nil });
            else if (isError)
                return new CobType(eCobType.Union, unionedTypes: new[] { type, Error });
            else if (isNil)
                return new CobType(eCobType.Union, unionedTypes: new[] { type, Nil });
            else
                return type;
        }

        // TODO Wow, what a horrible implementation
        public static bool TryParse(string typeName, out CobType result)
        {
            try
            {
                result = FromString(typeName);
                return true;
            }
            catch
            {
                result = null!;
                return false;
            }
        }

        //public CobType ToConcreteType(CobType bType) => this == eCobType.Generic ? bType : this;

        public CobType ToConcreteType(CobType bType)
        {
            // TODO Could be cleaned up
            if (this == eCobType.Generic)
                return bType;
            else if (this == eCobType.Union)
                return new CobType(eCobType.Union, unionedTypes: UnionedTypes.Select(x => x.ToConcreteType(bType)).ToList());
            else if (Tag is TupleType tupleType && tupleType.IsGeneric)
            {
                var tag = tupleType.FindConcretizedTuple(bType) ?? tupleType.AllocateConcretizedTuple(bType);
                return new CobType(Type, tag: tag);
            }
            else if (Tag is StructType structType && structType.IsGeneric)
            {
                var tag = structType.FindConcretizedStruct(bType) ?? structType.AllocateConcretizedStruct(bType);
                return new CobType(Type, tag: tag);
            }
            else
                return this;
        }

        public Type ToManagedType()
        {
            return Type switch
            {
                eCobType.Any => typeof(object),
                eCobType.Signed => Size switch
                {
                    -1 => Environment.Is64BitProcess ? typeof(long) : typeof(int),
                    8 => typeof(sbyte),
                    16 => typeof(short),
                    32 => typeof(int),
                    64 => typeof(long),
                    _ => throw new ArgumentOutOfRangeException()
                },
                eCobType.Unsigned => Size switch
                {
                    -1 => Environment.Is64BitProcess ? typeof(ulong) : typeof(uint),
                    8 => typeof(byte),
                    16 => typeof(ushort),
                    32 => typeof(uint),
                    64 => typeof(ulong),
                    _ => throw new ArgumentOutOfRangeException()
                },
                eCobType.Float => Size switch
                {
                    -1 => Environment.Is64BitProcess ? typeof(double) : typeof(float),
                    32 => typeof(float),
                    64 => typeof(double),
                    128 => typeof(decimal),
                    _ => throw new ArgumentOutOfRangeException()
                },
                eCobType.Array => this == String ? typeof(string) : ElementType.ToManagedType().MakeArrayType(),
                eCobType.Struct => Any.ToManagedType().MakeArrayType(), // TODO Not correct
                                                                        // Tag == Intrinsics.Array ? Any.ToManagedType().MakeArrayType() :  throw new NotImplementedException(), // ???
                eCobType.Tuple => throw new NotImplementedException(), // ???
                eCobType.Lens => throw new NotImplementedException(), // ???
                eCobType.Function => throw new NotImplementedException(), // ???
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        public Operand ToOperand(Artifact artifact)
        {
            long result = 0;
            int i = 0;

            Encode(this);

            return Operand._Type(result);

            void Encode(CobType type)
            {
                result |= ((long)type.Type & 0xF) << i; i += 4;
                switch (type.Type)
                {
                    case eCobType.None:
                    case eCobType.Any:
                    case eCobType.Boolean:
                    case eCobType.Error:
                    case eCobType.Nil:
                        // None
                        break;
                    case eCobType.Union:
                        // Count = SSS
                        result |= (type.UnionedTypes.Count & 0x7FF) << i; i += 3;
                        // UnionedTypes = for Count: <Type>
                        foreach (var subType in type.UnionedTypes)
                            Encode(subType);
                        break;
                    case eCobType.Signed:
                    case eCobType.Unsigned:
                    case eCobType.Float:
                    {
                        // Size = 1 << SSS
                        uint value = 0;
                        for (int x = type.Size; x > 1; x >>= 1)
                            ++value;
                        result |= value << i; i += 3;
                        break;
                    }
                    case eCobType.Array:
                    case eCobType.Lens:
                        // ElementType = <Type>
                        Encode(type.ElementType);
                        break;
                    case eCobType.Trait:
                        // ArtifactIndex = SSSS SSSS SSSS
                        result |= ((uint)artifact.TraitTypes.IndexOf((TraitType)type.Tag!) & 0xFFFFFF) << i; i += 12;
                        break;
                    case eCobType.Struct:
                        // ArtifactIndex = SSSS SSSS SSSS
                        result |= ((uint)artifact.StructTypes.IndexOf((StructType)type.Tag!) & 0xFFFFFF) << i; i += 12;
                        break;
                    case eCobType.Tuple:
                        // ArtifactIndex = SSSS SSSS SSSS
                        result |= ((uint)artifact.TupleTypes.IndexOf((TupleType)type.Tag!) & 0xFFFFFF) << i; i += 12;
                        break;
                    case eCobType.Function:
                        // ArtifactIndex = SSSS SSSS SSSS
                        result |= ((uint)artifact.Functions.IndexOf((Function)type.Tag!) & 0xFFFFFF) << i; i += 12;
                        break;
                    case eCobType.Module:
                        // ArtifactIndex = SSSS SSSS SSSS
                        result |= ((uint)artifact.Modules.IndexOf((Module)type.Tag!) & 0xFFFFFF) << i; i += 12;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        public static CobType FromOperand(Operand operand, Artifact artifact)
        {
            long value = operand.Value;
            int i = 0;

            return Decode();

            CobType Decode()
            {
                var type = (eCobType)((ulong)(value >> i) & 0xF); i += 4;
                switch (type)
                {
                    case eCobType.None:
                    case eCobType.Any:
                    case eCobType.Boolean:
                    case eCobType.Error:
                    case eCobType.Nil:
                        return new CobType(type);
                    case eCobType.Union:
                    {
                        // Count = SSS
                        int count = (int)((ulong)(value >> i) & 0x7); i += 3;
                        // UnionedTypes = for Count: <Type>
                        var unionedTypes = new List<CobType>();
                        while (count-- > 0)
                            unionedTypes.Add(Decode());
                        return new CobType(type, unionedTypes: unionedTypes);
                    }
                    case eCobType.Signed:
                    case eCobType.Unsigned:
                    case eCobType.Float:
                    {
                        // Size = 1 << SSS
                        int size = 1 << (int)((ulong)(value >> i) & 0x7); i += 3;
                        return new CobType(type, size: size);
                    }
                    case eCobType.Array:
                    case eCobType.Lens:
                    {
                        // ElementType = <Type>
                        var elementType = Decode();
                        if (elementType == Char)
                            return String;
                        return new CobType(type, elementType: elementType);
                    }
                    case eCobType.Trait:
                    {
                        // ArtifactIndex = SSSS SSSS SSSS
                        var idx = (int)((ulong)(value >> i) & 0xFFFFFF); i += 12;
                        var tag = artifact.TraitTypes[idx];
                        return new CobType(type, tag: tag);
                    }
                    case eCobType.Struct:
                    {
                        // ArtifactIndex = SSSS SSSS SSSS
                        var idx = (int)((ulong)(value >> i) & 0xFFFFFF); i += 12;
                        var tag = artifact.StructTypes[idx];
                        return new CobType(type, tag: tag);
                    }
                    case eCobType.Tuple:
                    {
                        // ArtifactIndex = SSSS SSSS SSSS
                        var idx = (int)((ulong)(value >> i) & 0xFFFFFF); i += 12;
                        var tag = artifact.TupleTypes[idx];
                        return new CobType(type, tag: tag);
                    }
                    case eCobType.Function:
                    {
                        // ArtifactIndex = SSSS SSSS SSSS
                        var idx = (int)((ulong)(value >> i) & 0xFFFFFF); i += 12;
                        var tag = artifact.Functions[idx];
                        return new CobType(type, tag: tag);
                    }
                    case eCobType.Module:
                    {
                        // ArtifactIndex = SSSS SSSS SSSS
                        var idx = (int)((ulong)(value >> i) & 0xFFFFFF); i += 12;
                        var tag = artifact.Modules[idx];
                        return new CobType(type, tag: tag);
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        public CobType Bust(CobType exclusion)
        {
            if (Type != eCobType.Union || UnionedTypes == null)
                throw new InvalidOperationException();

            var unionedTypes = UnionedTypes.Where(x => x.Type != exclusion).ToList();
            if (unionedTypes.Count == 0)
                return None;
            else if (unionedTypes.Count == 1)
                return unionedTypes[0];
            else
                return new CobType(eCobType.Union, unionedTypes: unionedTypes);
        }

        public IEnumerable<CobType> YieldTypesInUnion()
        {
            if (Type == eCobType.Union)
            {
                foreach (var type in UnionedTypes)
                    yield return type;
            }
            else
            {
                yield return new CobType(Type, Size, ElementType);
            }
        }

        public override string ToString()
        {
            if (AliasName != null)
                return AliasName;
            
            switch (Type)
            {
                case eCobType.Union:
                    return string.Join("|", UnionedTypes!.Select(x => x.ToString()));

                case eCobType.Signed:
                case eCobType.Unsigned:
                case eCobType.Float:
                    return $"{Type}.{Size}";

                case eCobType.None:
                case eCobType.Any:
                case eCobType.Boolean:
                case eCobType.Lens:
                case eCobType.Error:
                case eCobType.Nil:
                case eCobType.Generic:
                    return $"{Type}";

                case eCobType.Array:
                    return $"{Type}[{ElementType}]";

                case eCobType.Trait:
                case eCobType.Struct:
                case eCobType.Tuple:
                case eCobType.Function:
                case eCobType.Module:
                    return $"{Type}.{Tag}";

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static bool IsCastable(CobType srcType, CobType dstType)
        {
            if (srcType == dstType)
                return true;

            // NOTE any -> discrete requires runtime
            if (dstType == eCobType.Any || srcType == eCobType.Any)
                return true;

            if (dstType.Type == eCobType.Union)
            {
                if (srcType.Type == eCobType.Union)
                {
                    // All srcTypes must be in dstType
                    return srcType.UnionedTypes.All(x => dstType.UnionedTypes.Any(y => IsCastable(x, y)));
                }
                else
                    return dstType.UnionedTypes.Any(x => IsCastable(srcType, x));
            }

            if (srcType.Type is eCobType.Unsigned or eCobType.Signed or eCobType.Float
            &&  dstType.Type is eCobType.Unsigned or eCobType.Signed or eCobType.Float)
            {
                return true;
            }

            if (srcType.Type == eCobType.Struct && dstType.Type == eCobType.Trait)
            {
                var structType = srcType.Tag as StructType;
                return structType?.HasTrait(dstType.Tag as TraitType) ?? false;
            }
            else if (srcType.Type == eCobType.Tuple && dstType.Type == eCobType.Trait)
            {
                var tupleType = srcType.Tag as TupleType;
                return tupleType?.HasTrait(dstType.Tag as TraitType) ?? false;
            }

            if (srcType == eCobType.Error && dstType.HasErrorFlag)
                return true;

            // TODO Distant aliases
            // NOTE Immediate aliases should be functional as their type is directly encoded

            return false;
        }

        private readonly static Dictionary<string, CobType> Aliases = new();

        public static bool TryAddAlias(string name, CobType type)
        {
            if (Aliases.ContainsKey(name))
                return false;

            Aliases.Add(name, type);
            return true;
        }

        static CobType()
        {
            TryAddAlias("any", Any);
            TryAddAlias("bool", Boolean);
            TryAddAlias("int", Int);
            TryAddAlias("uint", UInt);
            TryAddAlias("float", Float);
            TryAddAlias("error", Error);
            TryAddAlias("nil", Nil);

            TryAddAlias("char", Char);
            //TryAddAlias("string", String);
        }
    }

    internal enum eCobType
    {
        None,
        Any,
        Union,
        Signed,
        Unsigned,
        Float,
        Boolean,
        Array, // TODO Remove
        Trait,
        Struct,
        Tuple,
        Lens,
        Function,
        Module,
        Error,
        Nil,

        Generic,

        Mask = 0x0F
    }

    internal sealed class StringContext : IScopeContext
    {
        public static readonly StringContext Instance = new ();

        public string Name => "string";

        public IScopeContext? Parent => null;

        public Compiler Compiler { get; set; }

        public ISymbol? FindSymbol(string name)
        {
            // TODO Immutable Field!
            if (name == "Length")
                return new Field(this, "Length", CobType.UInt, null, null);

            return null;
        }

        public Storage? EmitGetForSymbol(ISymbol symbol)
        {
            if (symbol is Field field && field.Name == "Length")
            {
                var storage = Compiler.CurrentFunction.AllocateRegisterStorage(CobType.U64);
                Compiler.CurrentFunction.Body.Emit(
                    Opcode.GetField,
                    storage.Operand,
                    Compiler.BinOpLHS.Operand,
                    Operand.ImmediateUnsigned(0)
                );

                return storage;
            }

            return null;
        }

        public bool EmitSetForSymbol(ISymbol symbol)
        {
            return false;
        }
    }
}