using System.Buffers;
using System.Diagnostics;
using Compiler.Ast.Expressions;
using Compiler.Lexer;

namespace Compiler.CodeGeneration.Artifacts
{
    [DebuggerDisplay("Variable {Name}: {Type} = {Value}")]
    internal record Variable : ISymbol, ILateTypeBinding
    {
        public IScopeContext? Owner { get; set; }

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
            //if (Type != eCobType.None)
            //    throw new InvalidOperationException($"Cannot late type rebind '{Name}', type is already bound.");

            Type = type;
        }

        public virtual bool IsVisibleTo(IScopeContext context) => true;

        public Variable DeepClone()
        {
            object? value;
            if (Value is Variable[] srcStructValue) 
            {
                // Structs are reference types
                if (Type.TagRecord != null && Type.TagRecord.Type == eRecordType.Struct)
                    return this;

                // Tuples are value types
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

    internal sealed class CobType : ISymbol, IEquatable<CobType>
    {
        public readonly static CobType None = new(eCobType.None, name: null);
        public readonly static CobType Any = new(eCobType.Any, name: "any");
        public readonly static CobType Function = new(eCobType.Function, name: "function");
        public readonly static CobType Boolean = new(eCobType.Boolean, name: "bool");
        public readonly static CobType Int = new (eCobType.Signed, -1, name: "int");
        public readonly static CobType S8 = new (eCobType.Signed, 8, name: "s8");
        public readonly static CobType S16 = new (eCobType.Signed, 16, name: "s16");
        public readonly static CobType S32 = new(eCobType.Signed, 32, name: "s32");
        public readonly static CobType S64 = new(eCobType.Signed, 64, name: "s64");
        public readonly static CobType UInt = new (eCobType.Unsigned, -1, name: "uint");
        public readonly static CobType U8 = new (eCobType.Unsigned, 8, name: "u8");
        public readonly static CobType U16 = new (eCobType.Unsigned, 16, name: "u16");
        public readonly static CobType U32 = new(eCobType.Unsigned, 32, name: "u32");
        public readonly static CobType U64 = new(eCobType.Unsigned, 64, name: "u64");
        public readonly static CobType Real = new (eCobType.Real, -1, name: "real");
        public readonly static CobType R32 = new(eCobType.Real, 32, name: "r32");
        public readonly static CobType R64 = new(eCobType.Real, 64, name: "r64");
        public readonly static CobType Module = new(eCobType.Module, name: null);
        public readonly static CobType Error = new(eCobType.Error, name: "error");
        public readonly static CobType Nil = new(eCobType.Nil, name: "nil");
        public readonly static CobType Generic = new(eCobType.Generic, name: null);
        public static CobType String => Intrinsics.StringType;

        public const int SizeUnadorned = -1;
        public const int SizePlatformSpecified = -2;

        public CobType? Parent { get; }

        public string? Name { get; }

        public eCobType Type { get; }

        public int Size { get; }

        public bool HasErrorFlag => Type == eCobType.Union && UnionedTypes!.Contains(Error);

        public bool HasNilFlag => Type == eCobType.Union && UnionedTypes!.Contains(Nil);

        public CobType? ElementType { get; }

        public IReadOnlyList<CobType>? UnionedTypes { get; }

        public object? Tag { get; }

        public Function? TagFunction => Tag as Function;

        public FunctionCandidates? TagFunctionCandidates => Tag as FunctionCandidates;

        public RecordType? TagRecord => Tag as RecordType;

        public Expression? Refinement { get; }

        CobType ISymbol.Type => this;

        bool ISymbol.Mutable => false;

        bool ISymbol.IsVisibleTo(IScopeContext context) => true; // TODO Not... really

        public CobType(
            eCobType type,
            int size = SizeUnadorned,
            CobType? elementType = null,
            IReadOnlyList<CobType>? unionedTypes = null,
            CobType? parent = null,
            string? name = null,
            object? tag = null,
            Expression? refinement = null
        ) {
            Parent = parent;
            Name = name;

            Type = type;
            Size = size;
            ElementType = elementType;
            UnionedTypes = unionedTypes;
            Tag = tag;
            Refinement = refinement;
        }

        public static bool operator ==(CobType? left, eCobType right) => !ReferenceEquals(left, null) && left.Type == right;
        public static bool operator !=(CobType? left, eCobType right) => ReferenceEquals(left, null) || left.Type != right;
        public static bool operator ==(CobType? left, CobType? right) => Equals(left, right);
        public static bool operator !=(CobType left, CobType? right) => !Equals(left, right);
        public bool Equals(CobType? other)
        {
            if (other is null)
                return false;
            if (ReferenceEquals(this, other))
                return true;

            if (Type == eCobType.Distinct) // Distinct types must equal by reference
                return false;

            // TODO Primitives should not be equal on reference but on identity, will simplify a lot of code
            return Equals(Parent, other.Parent)
                && Type == other.Type
                && Size == other.Size
                && Equals(ElementType, other.ElementType)
                && Equals(UnionedTypes, other.UnionedTypes)
                && Equals(Tag, other.Tag);
        }

        public override bool Equals(object? obj)
        {
            return ReferenceEquals(this, obj) || obj is CobType other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine((int)Type, Size, ElementType, Tag);
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

                if (ident == "This" && context != null)
                    ident = context.Name;

                if (ident == "any")
                    type = Any;
                else if (ident == "bool")
                    type = Boolean;
                else if (ident == "int")
                    type = Int;
                else if (ident == "s8")
                    type = S8;
                else if (ident == "s16")
                    type = S16;
                else if (ident == "s32")
                    type = S32;
                else if (ident == "s64")
                    type = S64;
                else if (ident == "uint")
                    type = UInt;
                else if (ident == "u8")
                    type = U8;
                else if (ident == "u16")
                    type = U16;
                else if (ident == "u32")
                    type = U32;
                else if (ident == "u64")
                    type = U64;
                else if (ident == "real")
                    type = Real;
                else if (ident == "r32")
                    type = R32;
                else if (ident == "r64")
                    type = R64;
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
                            RecordType? recordType;
                            CobType? distinctType;
                            if ((recordType = module.FindRecordType(ident)) != null)
                                type = recordType.ThisType;
                            else if ((distinctType = module.FindDistinctType(ident)) != null)
                                type = distinctType;
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
                var ctx = context;
                while (ctx != null && ctx.Parent != null)
                    ctx = ctx.Parent;

                // TODO Might actually need to allocate on the CurrentContext instead for namespacing purposes
                var rootModule = (Module)ctx!; // NOTE Root is always $root module
                var recordTypeIdentifier = typeName.Record!.Name;
                var recordType = rootModule.FindRecordType(recordTypeIdentifier)
                              ?? rootModule.AllocateRecordType(recordTypeIdentifier, typeName.Record.Type);

                type = new CobType(eCobType.Record, tag: recordType);
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
                var tag = Intrinsics.Array.TagRecord.FindConcretizedRecord(type) ?? Intrinsics.Array.TagRecord.AllocateConcretizedRecord(type);
                type = new CobType(eCobType.Record, tag: tag);
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
            else if (this == eCobType.Function)
            {
                var tag = Tag as FunctionSignature;
                if (tag != null)
                {
                    tag = new FunctionSignature
                    {
                        Parameters = tag.Parameters.Select(
                            x => new Function.Parameter(x.Name, x.Type.ToConcreteType(bType), x.IsSpread)
                        ).ToList(),
                        ReturnType = tag.ReturnType.ToConcreteType(bType)
                    };
                }

                return new CobType(Type, Size, ElementType, UnionedTypes, Parent, Name, tag);
            }
            else
                return this;
        }

        // TODO This should probably be moved elsewhere
        public CobType ToImpliedType(Compiler compiler)
        {
            //if (Type is eCobType.Signed or eCobType.Unsigned or eCobType.Real
            //&&  Size is SizeUnadorned or SizePlatformSpecified)
            //{
            //    var minSize = ArtifactFactory.ResolveMinimumBusWidthForSpecifiedArchitectures(compiler.ArtifactDirectives);
            //    return new CobType(Type, minSize);
            //}

            return this;
        }

        public Type ToManagedType()
        {
            return Type switch
            {
                eCobType.Any => typeof(object),
                eCobType.Signed => Size switch
                {
                    SizePlatformSpecified => Environment.Is64BitProcess ? typeof(long) : typeof(int),
                    SizeUnadorned => Environment.Is64BitProcess ? typeof(long) : typeof(int),
                    8 => typeof(sbyte),
                    16 => typeof(short),
                    32 => typeof(int),
                    64 => typeof(long),
                    _ => throw new ArgumentOutOfRangeException()
                },
                eCobType.Unsigned => Size switch
                {
                    SizePlatformSpecified => Environment.Is64BitProcess ? typeof(ulong) : typeof(uint),
                    SizeUnadorned => Environment.Is64BitProcess ? typeof(ulong) : typeof(uint),
                    8 => typeof(byte),
                    16 => typeof(ushort),
                    32 => typeof(uint),
                    64 => typeof(ulong),
                    _ => throw new ArgumentOutOfRangeException()
                },
                eCobType.Real => Size switch
                {
                    SizePlatformSpecified => Environment.Is64BitProcess ? typeof(double) : typeof(float),
                    SizeUnadorned => Environment.Is64BitProcess ? typeof(double) : typeof(float),
                    32 => typeof(float),
                    64 => typeof(double),
                    128 => typeof(decimal),
                    _ => throw new ArgumentOutOfRangeException()
                },
                eCobType.Record => Any.ToManagedType().MakeArrayType(), // TODO Not correct
                                                                        // Tag == Intrinsics.Array ? Any.ToManagedType().MakeArrayType() :  throw new NotImplementedException(), // ???
                eCobType.Function => throw new NotImplementedException(), // ???
                eCobType.Distinct => Parent?.ToManagedType() ?? throw new InvalidOperationException(),
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
                    case eCobType.Real:
                    {
                        // Size = 1 << SSS
                        uint value = 0;
                        for (int x = type.Size; x > 1; x >>= 1)
                            ++value;
                        result |= value << i; i += 3;
                        break;
                    }
                    case eCobType.Record:
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
                    case eCobType.Distinct:
                        // ArtifactIndex = SSSS SSSS SSSS
                        result |= ((uint)artifact.Types.IndexOf(type) & 0xFFFFFF) << i; i += 12;
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
                        return Any;
                    case eCobType.Boolean:
                        return Boolean;
                    case eCobType.Error:
                        return Error;
                    case eCobType.Nil:
                        return Nil;
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
                    case eCobType.Real:
                    {
                        // Size = 1 << SSS
                        int size = (int)((ulong)(value >> i) & 0x7); i += 3;
                            size = size != 0 ? 1 << size : SizeUnadorned; // TODO PlatformSize
                        if (type == eCobType.Signed)
                        {
                            if (size == -1)
                                return Int;
                            else if (size == 8)
                                return S8;
                            else if (size == 16)
                                return S16;
                            else if (size == 32)
                                return S32;
                            else if (size == 64)
                                return S64;
                        }
                        else if (type == eCobType.Unsigned)
                        {
                            if (size == -1)
                                return UInt;
                            else if (size == 8)
                                return U8;
                            else if (size == 16)
                                return U16;
                            else if (size == 32)
                                return U32;
                            else if (size == 64)
                                return U64;
                        }
                        else if (type == eCobType.Real)
                        {
                            if (size == -1)
                                return Real;
                            else if (size == 32)
                                return R32;
                            else if (size == 64)
                                return R64;
                        }
                        return new CobType(type, size: size);
                    }
                    case eCobType.Record:
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
                    case eCobType.Distinct:
                    {
                        // ArtifactIndex = SSSS SSSS SSSS
                        var idx = (int)((ulong)(value >> i) & 0xFFFFFF); i += 12;
                        return artifact.Types[idx];
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        public CobType Bust(CobType exclusion)
        {
            if (Type != eCobType.Union || UnionedTypes == null)
                return None; //throw new InvalidOperationException();

            var unionedTypes = UnionedTypes.Where(x => x != exclusion).ToList();
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
                yield return this;
            }
        }

        // TODO Places where this is used might actually want the full ancestry
        public CobType? FirstAncestorWhere(Func<CobType, bool> predicate)
        {
            CobType? x = this;
            while (x != null)
            {
                if (predicate(x))
                    return x;

                x = x.Parent;
            }

            return null;
        }

        public override string ToString()
        {
            if (HasErrorFlag)
                return Bust(Error).ToString() + '!';
            if (HasNilFlag)
                return Bust(Nil).ToString() + '?';
            if (Name != null)
                return Name;

            switch (Type)
            {
                case eCobType.Union:
                    return string.Join("|", UnionedTypes!.Select(x => x.ToString()));

                case eCobType.Signed:
                    return Size == SizeUnadorned ? "int" : Size == SizePlatformSpecified ? "s" : $"s{Size}";
                case eCobType.Unsigned:
                    return Size == SizeUnadorned ? "uint" : Size == SizePlatformSpecified ? "u" : $"u{Size}";
                case eCobType.Real:
                    return Size == SizeUnadorned ? "real" : Size == SizePlatformSpecified ? "r" : $"r{Size}";

                case eCobType.Any:
                    return "any";
                case eCobType.Boolean:
                    return "bool";
                case eCobType.Error:
                    return "error";
                case eCobType.Nil:
                    return "nil";
                case eCobType.Generic:
                    return "`T"; // TODO??

                case eCobType.Record:
                case eCobType.Function:
                case eCobType.Module:
                    return Tag?.ToString() ?? $"(NULL {Type.ToString()})";

                case eCobType.Distinct:
                    return "(distinct)";

                case eCobType.None:
                    return "(none)";

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        // TODO Passing an optional long srcValue for integer types specifically is very awkward, should find
        //      some way to universalize or specialize the function signatures below...

        // TODO IsExplicitCastable needs to also validate refinements

        // dst = src
        public static bool IsExplicitCastable(CobType dstType, CobType srcType, long? srcValue = null)
        {
            //
            // SPECIAL CONDITIONS
            if (dstType == srcType)
                return true;

            //
            // ACTUAL TYPE CAST CHECK LOGIC

            // - All implicit casts may be explicit
            if (IsImplicitCastable(dstType, srcType, srcValue))
                return true;

            // - A super-type may explicit cast only to immediate sub-types
            if (dstType.Parent == srcType)
                return true;

            // - A sub-type may explicit cast to any ancestor super-type
            var x = srcType.Parent;
            while (x != null)
            {
                if (x == dstType)
                    return true;

                // HACK Handle implicit casts between number types
                if (dstType.Type is eCobType.Unsigned or eCobType.Signed or eCobType.Real
                &&  x.Type is eCobType.Unsigned or eCobType.Signed or eCobType.Real
                &&  IsImplicitCastable(dstType, x, srcValue))
                    return true;

                x = x.Parent;
            }

            // NUMBERS
            if (dstType.Type is eCobType.Unsigned or eCobType.Signed or eCobType.Real
            &&  srcType.Type is eCobType.Unsigned or eCobType.Signed or eCobType.Real)
                return true;

            return false;
        }

        // dst = src
        public static bool IsImplicitCastable(CobType dstType, CobType srcType, long? srcValue = null)
        {
            //
            // SPECIAL CONDITIONS
            if (dstType == srcType)
                return true;

            // NOTE any -> discrete requires runtime
            if (dstType == eCobType.Any || srcType == eCobType.Any)
                return true;

            if (dstType.Type == eCobType.Union)
            {
                if (srcType.Type == eCobType.Union)
                {
                    // All srcTypes must be in dstType
                    return srcType.UnionedTypes.All(x => dstType.UnionedTypes.Any(y => IsImplicitCastable(x, y)));
                }
                else
                    return dstType.UnionedTypes.Any(x => IsImplicitCastable(x, srcType));
            }

            if (dstType == eCobType.Distinct && dstType.Parent != null)
                return IsImplicitCastable(dstType.Parent, srcType, srcValue);

            //
            // ACTUAL TYPE CAST CHECK LOGIC

            // NUMBERS
            if (dstType.Type is eCobType.Unsigned or eCobType.Signed
            &&  srcType.Type is eCobType.Unsigned or eCobType.Signed)
            {
                if (dstType.Type == eCobType.Unsigned && srcType.Type == eCobType.Signed)
                    return false;

                // NOTE Casts where data MIGHT be lost when only the types are known are not permitted, if a more
                //      precise result is required then IsImplicitCastable(CobType, Variable) should be used instead
                if (!IsIntegerTypeCapableOfStoringValueWithoutTruncation(dstType, srcType, srcValue))
                    return false;

                return true;

                //if (srcType.Size == SizeUnadorned || dstType.Size == SizeUnadorned)
                //    return true;

                //if (srcType.Type != dstType.Type)
                //    return false;

                //return dstType.Size >= srcType.Size; // u64 >= u32
            }


            if (dstType.TagRecord != null && dstType.TagRecord.IsTrait
            &&  srcType.Type is eCobType.Record or eCobType.Distinct)
            {
                var recordType = srcType.Tag as RecordType;
                return recordType?.HasTrait(dstType.Tag as RecordType) ?? false;
            }

            if (dstType.Type == eCobType.Record && dstType.TagRecord.Type == eRecordType.Tuple
            &&  srcType.Type == eCobType.Record && srcType.TagRecord.Type == eRecordType.Tuple)
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

            return false;
        }

        private static readonly long[] SignedMin = { sbyte.MinValue, short.MinValue, int.MinValue, int.MinValue, long.MinValue, long.MinValue, long.MinValue, long.MinValue };
        private static readonly long[] SignedMax = { (long)sbyte.MaxValue, (long)short.MaxValue, int.MaxValue, int.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue };
        private static readonly long[] UnsignedMin = { 0, 0, 0, 0, 0, 0, 0, 0 };
        private static readonly long[] UnsignedMax = { byte.MaxValue, ushort.MaxValue, uint.MaxValue, uint.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue  };
        public static bool IsIntegerTypeCapableOfStoringValueWithoutTruncation(CobType dstType, CobType srcType, long? srcValue)
        {
            if (dstType.Size == SizeUnadorned)
                return true;

            if (srcType.Size == SizeUnadorned)
            {
                if (srcValue != null)
                {
                    // if WillNotTruncateValue
                    var minTable = dstType.Type == eCobType.Signed ? SignedMin : UnsignedMin;
                    var maxTable = dstType.Type == eCobType.Signed ? SignedMax : UnsignedMax;
                    var min = minTable[dstType.Size / 8 - 1];
                    var max = maxTable[dstType.Size / 8 - 1];

                    // TODO Technically doesn't work for u64 with MSB set
                    return srcValue >= min && srcValue <= max;

                    //if (dstType == eCobType.Real && Type.Type == eCobType.Real)
                    //{
                    //    var a = BitConverter.Int64BitsToDouble(Operand.Value);
                    //    if (dstType.Size == 32)
                    //        return ((double)(float)a).Equals(a);
                    //    else if (dstType.Size == 64)
                    //        return true;
                    //}
                }
                else
                    return true;
            }
            else if (dstType.Size >= srcType.Size)
                return true;

            return false;
        }

        public static CobType ResolveOperatorResultType(TokenType @operator, CobType? lhsType, CobType? rhsType)
        {
            if (lhsType == null || rhsType == null)
                return None;

            switch (@operator)
            {
                case TokenType.BitPack:
                    if (lhsType != eCobType.Signed && lhsType != eCobType.Unsigned)
                        return None;
                    if (rhsType != eCobType.Signed && rhsType != eCobType.Unsigned)
                        return None;
                    return lhsType;

                case TokenType.Range:
                case TokenType.RangeInclusive:
                case TokenType.RangeLength:
                case TokenType.RangeTerminal:
                    if (lhsType != eCobType.Signed && lhsType != eCobType.Unsigned && lhsType != eCobType.Real)
                        return None;
                    if (rhsType != eCobType.Signed && rhsType != eCobType.Unsigned && rhsType != eCobType.Real)
                        return None;
                    return Intrinsics.Range;

                case TokenType.ConditionalAnd:
                case TokenType.ConditionalOr:
                    if (lhsType != Boolean || rhsType != Boolean)
                        return None;
                    return Boolean;

                case TokenType.Equals:
                case TokenType.NotEquals:
                    if (lhsType == rhsType)
                        return Boolean;
                    if (lhsType != eCobType.Signed && lhsType != eCobType.Unsigned && lhsType != eCobType.Real)
                        return None;
                    if (rhsType != eCobType.Signed && rhsType != eCobType.Unsigned && rhsType != eCobType.Real)
                        return None;
                    return Boolean;

                case TokenType.LessThan:
                case TokenType.LessThanOrEquals:
                case TokenType.MoreThan:
                case TokenType.MoreThanOrEquals:
                    if (lhsType != eCobType.Signed && lhsType != eCobType.Unsigned && lhsType != eCobType.Real)
                        return None;
                    if (rhsType != eCobType.Signed && rhsType != eCobType.Unsigned && rhsType != eCobType.Real)
                        return None;
                    return Boolean;

                case TokenType.Add:
                case TokenType.Subtract:
                case TokenType.Multiply:
                case TokenType.Exponent:
                case TokenType.Divide:
                case TokenType.DivideCeil:
                case TokenType.DivideFloor:
                case TokenType.Remainder:
                case TokenType.Modulo:
                case TokenType.AddAssign:
                case TokenType.SubtractAssign:
                case TokenType.MultiplyAssign:
                case TokenType.DivideAssign:
                case TokenType.DivideCeilAssign:
                case TokenType.DivideFloorAssign:
                case TokenType.RemainderAssign:
                case TokenType.ModuloAssign:
                    if (lhsType != eCobType.Signed && lhsType != eCobType.Unsigned && lhsType != eCobType.Real)
                        return None;
                    if (rhsType != eCobType.Signed && rhsType != eCobType.Unsigned && rhsType != eCobType.Real)
                        return None;
                    // implicit casts to lose the least info: uint -> int -> real
                    if (lhsType == eCobType.Real || rhsType == eCobType.Real)
                        return Real;
                    if (lhsType == eCobType.Signed || rhsType == eCobType.Signed)
                        return Int;
                    if (lhsType == eCobType.Unsigned || rhsType == eCobType.Unsigned)
                        return UInt;
                    return None;

                case TokenType.BitLeftShift:
                case TokenType.BitRightShift:
                case TokenType.BitLeftRotate:
                case TokenType.BitRightRotate:
                case TokenType.BitAnd:
                case TokenType.BitOr:
                case TokenType.BitXor:
                case TokenType.BitLeftShiftAssign:
                case TokenType.BitRightShiftAssign:
                case TokenType.BitLeftRotateAssign:
                case TokenType.BitRightRotateAssign:
                case TokenType.BitAndAssign:
                case TokenType.BitOrAssign:
                case TokenType.BitXorAssign:
                    //if (lhsType != eCobType.Signed && lhsType != eCobType.Unsigned)
                    //    return None;
                    //if (rhsType != eCobType.Signed && rhsType != eCobType.Unsigned)
                    //    return None;
                    if (!IsExplicitCastable(Int, lhsType) || !IsExplicitCastable(Int, rhsType))
                        return None;
                    return lhsType;

                default:
                    return None;
            }
        }
    }

    internal enum eCobType
    {
        None,
        Any,
        Union,
        Record,
        Signed,
        Unsigned,
        Real,
        Boolean,
        Function,
        Module,
        Error,
        Nil,
        Generic,
        Distinct,
        Mask = 0x0F
    }

    // TODO Are "Signatures" the right way to implement this?
    internal sealed class FunctionSignature
    {
        public CobType ReturnType { get; init; }

        public IReadOnlyList<Function.Parameter> Parameters { get; init; }
    }
}