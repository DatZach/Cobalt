using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Compiler.Lexer;

namespace Compiler.CodeGeneration.Artifacts
{
    [DebuggerDisplay("Variable {Name}: {Type} = {Value}")]
    internal record Variable : ISymbol, ILateTypeBinding
    {
        public string Name { get; }

        public CobType Type { get; private set; }

        public bool Mutable { get; }

        public object? Value { get; set; }

        public Variable[] RecordValue
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

        void ILateTypeBinding.RebindType(CobType type)
        {
            if (Type != eCobType.None)
                throw new InvalidOperationException($"Cannot late type rebind '{Name}', type is already bound.");

            Type = type;
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
            else if (Value is Function srcFunction)
                value = srcFunction;
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
            var value = Value?.ToString() ?? "(null)";
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

    internal interface ILateTypeBinding
    {
        void RebindType(CobType type);
    }

    internal sealed record CobType
    {
        public readonly static CobType None = eCobType.None;
        public readonly static CobType Any = eCobType.Any;
        public readonly static CobType Function = eCobType.Function;
        public readonly static CobType Boolean = new(eCobType.Boolean);
        public readonly static CobType Int = new (eCobType.Signed, -1);
        public readonly static CobType UInt = new (eCobType.Unsigned, -1);
        public readonly static CobType Float = new (eCobType.Float, -1);
        public readonly static CobType U8 = new (eCobType.Unsigned, 8);
        public readonly static CobType U32 = new(eCobType.Unsigned, 32);
        public readonly static CobType U64 = new(eCobType.Unsigned, 64);
        public readonly static CobType Module = eCobType.Module;
        public readonly static CobType Error = eCobType.Error;
        public readonly static CobType Nil = eCobType.Nil;
        public readonly static CobType Generic = eCobType.Generic;

        public eCobType Type { get; }

        public int Size { get; }

        public bool HasErrorFlag => Type == eCobType.Union && UnionedTypes!.Contains(Error);

        public bool HasNilFlag => Type == eCobType.Union && UnionedTypes!.Contains(Nil);

        public CobType? ElementType { get; }

        public IReadOnlyList<CobType>? UnionedTypes { get; }

        public object? Tag { get; }

        public Function? TagFunction => Tag as Function;

        public FunctionCandidates? TagFunctionCandidates => Tag as FunctionCandidates;

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

        // TODO Return CobType? and have consumers check if null for proper error reporting
        public static CobType FromTypeName(TypeName? typeName, IScopeContext? context)
        {
            if (typeName == null)
                return None;

            CobType type;

            if (typeName.Type == eTypeName.Identifier)
            {
                var ident = typeName.Identifier!;
                if (ident.Length >= 2 && ident[0] == 's' && char.IsDigit(ident[1]))
                    type = new CobType(eCobType.Signed, int.Parse(ident[1..]));
                else if (ident.Length >= 2 && ident[0] == 'u' && char.IsDigit(ident[1]))
                    type = new CobType(eCobType.Unsigned, int.Parse(ident[1..]));
                else if (ident.Length >= 2 &&  ident[0] == 'f' && char.IsDigit(ident[1]))
                    type = new CobType(eCobType.Float, int.Parse(ident[1..]));
                else if (ident == "any")
                    type = Any;
                else if (ident == "bool")
                    type = Boolean;
                else if (ident == "int")
                    type = Int;
                else if (ident == "uint")
                    type = UInt;
                else if (ident == "float")
                    type = Float;
                else if (ident == "error")
                    type = Error;
                else if (ident == "nil")
                    type = Nil;
                else // User-defined type identifier
                {
                    type = null!;
                    var ctx = context;
                    while (ctx != null && type == null)
                    {
                        if (ctx is Module module)
                        {
                            TraitType? traitType;
                            RecordType? recordType;
                            DistinctType? distinctType;
                            if ((traitType = module.FindTraitType(ident)) != null)
                                type = new CobType(eCobType.Trait, tag: traitType);
                            else if ((recordType = module.FindRecordType(ident)) != null)
                                type = recordType.ThisType;
                            else if ((distinctType = module.FindDistinctType(ident)) != null)
                                type = distinctType.SubType;
                        }
                        else if (ctx is RecordType recordType)
                        {
                            CobType? aliasType;
                            if ((aliasType = recordType.FindGenericType(ident)) != null)
                                type = aliasType;
                        }

                        ctx = ctx.Parent;
                    }

                    if (type == null)
                        throw new Exception($"The typename '{typeName}' is not valid");
                }
            }
            else if (typeName.Type == eTypeName.FunctionSignature)
            {
                var parameters = typeName.Function.Parameters.Select(
                    x => new Function.Parameter(x.Name, FromTypeName(x.TypeName, context), x.IsSpread)
                ).ToList();
                var returnType = FromTypeName(typeName.Function.ReturnTypeName, context);

                type = new CobType(eCobType.Function, tag: new FunctionSignature
                {
                    Parameters = parameters,
                    ReturnType = returnType
                });
            }
            else if (typeName.Type == eTypeName.RecordSignature)
            {
                // TODO This might be wrong, can allocate records in records
                Module? module = null;
                var ctx = context;
                while (ctx != null && module == null)
                {
                    module = ctx as Module;
                    ctx = ctx.Parent;
                }

                if (module == null)
                    throw new Exception("Tuple Signature declaration is not valid here");

                var recordTypeIdentifier = $"InlineTuple'{typeName.Record.UniqueId}";
                var tupleType = module.FindRecordType(recordTypeIdentifier);
                if (tupleType == null)
                {
                    tupleType = module.AllocateRecordType(recordTypeIdentifier, eRecordType.Tuple);

                    foreach (var x in typeName.Record.Fields)
                    {
                        tupleType.AllocateField(x.Name ?? "", FromTypeName(x.TypeName, tupleType), false, false);
                    }
                }

                type = new CobType(eCobType.Tuple, tag: tupleType);
            }
            else
                throw new ArgumentOutOfRangeException();

            // Resolve Generic

            CobType bType;
            if (typeName.Generic != null && typeName.Generic.Count > 0
            &&  (bType = FromTypeName(typeName.Generic[0], context)) != None)
            {
                if (type.Tag is RecordType recordType)
                {
                    // TODO Might be better to just merge these methods into a single one
                    var tag = recordType.FindConcretizedRecord(bType) ?? recordType.AllocateConcretizedRecord(bType);
                    type = tag.ThisType;
                }
                else
                    throw new Exception($"The typename '{typeName}' is not valid");
            }

            // Suffixes
            if (typeName.IsArray)
            {
                var tag = Intrinsics.Array.FindConcretizedRecord(type) ?? Intrinsics.Array.AllocateConcretizedRecord(type);
                type = new CobType(eCobType.Struct, tag: tag);
            }

            if (typeName.Union != null)
            {
                var unionedTypes = new List<CobType> { type };
                for (var nextType = typeName.Union; nextType != null; nextType = nextType.Union)
                {
                    type = FromTypeName(typeName.Union, context);
                    unionedTypes.Add(type);
                }

                type = new CobType(
                    eCobType.Union,
                    unionedTypes: unionedTypes
                );
            }
            
            if (typeName.IsErrorable && typeName.IsNillable)
                return new CobType(eCobType.Union, unionedTypes: new[] { type, Error, Nil });
            else if (typeName.IsErrorable)
                return new CobType(eCobType.Union, unionedTypes: new[] { type, Error });
            else if (typeName.IsNillable)
                return new CobType(eCobType.Union, unionedTypes: new[] { type, Nil });
            else
                return type;
        }

        public static bool TryParse(TypeName typeName, IScopeContext? context, out CobType result)
        {
            try
            {
                result = FromTypeName(typeName, context);
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
            else if (Tag is RecordType recordType && recordType.IsGeneric)
            {
                var tag = recordType.FindConcretizedRecord(bType) ?? recordType.AllocateConcretizedRecord(bType);
                return tag.ThisType; // new CobType(Type, tag: tag);
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
                eCobType.Array => ElementType.ToManagedType().MakeArrayType(),
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
                    case eCobType.Generic:
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
                        result |= ((uint)artifact.RecordTypes.IndexOf((RecordType)type.Tag!) & 0xFFFFFF) << i; i += 12;
                        break;
                    case eCobType.Tuple:
                        // ArtifactIndex = SSSS SSSS SSSS
                        result |= ((uint)artifact.RecordTypes.IndexOf((RecordType)type.Tag!) & 0xFFFFFF) << i; i += 12;
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
                        var tag = artifact.RecordTypes[idx];
                        return new CobType(type, tag: tag);
                    }
                    case eCobType.Tuple:
                    {
                        // ArtifactIndex = SSSS SSSS SSSS
                        var idx = (int)((ulong)(value >> i) & 0xFFFFFF); i += 12;
                        var tag = artifact.RecordTypes[idx];
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

            if (srcType.Type is eCobType.Tuple or eCobType.Struct && dstType.Type == eCobType.Trait)
            {
                var recordType = srcType.Tag as RecordType;
                return recordType?.HasTrait(dstType.Tag as TraitType) ?? false;
            }

            if (srcType.Type == eCobType.Tuple && dstType.Type == eCobType.Tuple)
            {
                if (srcType.Tag == dstType.Tag)
                    return true;

                if (srcType.Tag is RecordType srcRecordType && srcRecordType.IsAnonymous
                &&  dstType.Tag is RecordType dstRecordType)
                {
                    return dstRecordType.IsFieldSignatureMatch(srcRecordType);
                }

                return false;
            }

            if (srcType == eCobType.Error && dstType.HasErrorFlag)
                return true;

            // TODO HACK Not implemented correctly; Check signature
            if (srcType == eCobType.Function && dstType == eCobType.Function && dstType.Tag is FunctionSignature)
                return true;

            // TODO Distant aliases
            // NOTE Immediate aliases should be functional as their type is directly encoded

            return false;
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
        Struct, // TODO Merge Struct+Tuple -> Record?
        Tuple,
        Lens,
        Function,
        Module,
        Error,
        Nil,

        Generic,

        Mask = 0x0F
    }

    // TODO Are "Signatures" the right way to implement this?
    internal sealed class FunctionSignature
    {
        public CobType ReturnType { get; init; }

        public IReadOnlyList<Function.Parameter> Parameters { get; init; }
    }
}