using Compiler.Ast.Expressions;

namespace Compiler.CodeGeneration
{
    internal sealed record CobVariable
    {
        public string Name { get; }

        public CobType Type { get; }

        public bool Mutable { get; }

        public object? Value { get; set; }

        public CobVariable[] StructValue
        {
            get => Value as CobVariable[] ?? throw new InvalidDataException();
            set => Value = value;
        }

        public byte[] BufferValue
        {
            get => Value as byte[] ?? throw new InvalidDataException();
            set => Value = value;
        }

        public long IntValue
        {
            get => (long)Value;
            set => Value = value;
        }

        // TODO Should assume variables are mutable by default

        public CobVariable(string name, CobType type, bool mutable)
        {
            Name = name;
            Type = type;
            Mutable = mutable;
        }

        public CobVariable(string name, CobType type, bool mutable, long intValue)
        {
            Name = name;
            Type = type;
            Mutable = mutable;
            IntValue = intValue;
        }

        public override string ToString()
        {
            return $"{Name,-25}{Type} = {Value}";
        }

        public CobVariable DeepClone()
        {
            object? value;
            if (Value is CobVariable[] srcStructValue)
            {
                var dstStructValue = new CobVariable[srcStructValue.Length];
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

            // TODO Does Type need to be cloned?
            return this with { Value = value };
        }
    }

    internal sealed record CobType
    {
        public readonly static CobType None = eCobType.None;
        public readonly static CobType Any = eCobType.Any;
        public readonly static CobType Func = eCobType.Function;
        public readonly static CobType Int = new (eCobType.Signed, -1);
        public readonly static CobType UInt = new (eCobType.Unsigned, -1);
        public readonly static CobType Float = new (eCobType.Float, -1);
        public readonly static CobType U8 = new (eCobType.Unsigned, 8);
        public readonly static CobType U32 = new(eCobType.Unsigned, 32);
        public readonly static CobType U64 = new(eCobType.Unsigned, 64);
        public readonly static CobType Char = new (eCobType.Unsigned, 8) { AliasName = "char" };
        public readonly static CobType String = new (eCobType.Array, elementType: Char, tag: StringContext.Instance) { AliasName = "string" };
        public readonly static CobType Module = eCobType.Module;

        public string? AliasName { get; init; } // TODO Remove?

        public eCobType Type { get; }

        public int Size { get; }

        public CobType? ElementType { get; }

        public object? Tag { get; }

        public Function? Function => Tag as Function;

        public CobType(eCobType type, int size = -1, CobType? elementType = null, object? tag = null)
        {
            Type = type;
            Size = size;
            ElementType = elementType;
            Tag = tag;
        }

        public CobType(eCobType type, CobType elementType)
        {
            // TODO Validate "Container" types?
            Type = type;
            ElementType = elementType;
            Size = -1;
        }

        public static implicit operator CobType(eCobType type)
        {
            return new CobType(type, -1);
        }
        
        public override int GetHashCode()
        {
            return HashCode.Combine((int)Type, Size, ElementType, Function);
        }

        //public static bool operator ==(CobType left, CobType right)
        //{
        //    return left.Type == right.Type && left.Size == right.Size;
        //}

        // TODO Needed?
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

        public static CobType FromString(string? typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return None;

            var isArray = typeName.EndsWith("[]");
            if (isArray)
            {
                var elementType = FromString(typeName[..^2]);
                return new CobType(eCobType.Array, elementType);
            }
            
            if (typeName.Length >= 2)
            {
                if (typeName[0] == 's' && char.IsDigit(typeName[1]))
                    return new CobType(eCobType.Signed, int.Parse(typeName[1..]));
                if (typeName[0] == 'u' && char.IsDigit(typeName[1]))
                    return new CobType(eCobType.Unsigned, int.Parse(typeName[1..]));
                if (typeName[0] == 'f' && char.IsDigit(typeName[1]))
                    return new CobType(eCobType.Float, int.Parse(typeName[1..]));
                if (typeName[0] == 'f' && typeName[1] == 'n')
                    return new CobType(eCobType.Function, -1);
                if (typeName == "int")
                    return Int;
                if (typeName == "uint")
                    return UInt;
                if (typeName == "any")
                    return Any;
                if (Aliases.TryGetValue(typeName, out var aliasType))
                    return aliasType;
            }

            throw new Exception($"Illegal type definition '{typeName}'");
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

        public Type ToManagedType()
        {
            return Type switch
            {
                eCobType.Any => typeof(object),
                eCobType.Signed => Size switch
                {
                    8 => typeof(sbyte),
                    16 => typeof(short),
                    32 => typeof(int),
                    64 => typeof(long)
                },
                eCobType.Unsigned => Size switch
                {
                    8 => typeof(byte),
                    16 => typeof(ushort),
                    32 => typeof(uint),
                    64 => typeof(ulong)
                },
                eCobType.Float => Size switch
                {
                    32 => typeof(float),
                    64 => typeof(double),
                    128 => typeof(decimal)
                },
                eCobType.Array => this == String ? typeof(string) : ElementType.ToManagedType().MakeArrayType(),
                eCobType.Struct => throw new NotImplementedException(), // ???
                eCobType.Tuple => throw new NotImplementedException(), // ???
                eCobType.Lens => throw new NotImplementedException(), // ???
                eCobType.Function => throw new NotImplementedException(), // ???
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        public override string ToString()
        {
            if (AliasName != null)
                return AliasName;
            
            if (ElementType != null)
                return $"{Type}[{ElementType}]";

            return $"{Type}.{Size}";
        }

        public static bool IsCastable(CobType srcType, CobType dstType)
        {
            if (srcType == dstType)
                return true;

            // NOTE any -> discrete requires runtime
            if (dstType == eCobType.Any || srcType == eCobType.Any)
                return true;

            if (srcType.Type is eCobType.Unsigned or eCobType.Signed or eCobType.Float
            &&  dstType.Type is eCobType.Unsigned or eCobType.Signed or eCobType.Float)
            {
                return true;
            }

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
            TryAddAlias("char", Char);
            TryAddAlias("string", String);
        }
    }

    internal enum eCobType
    {
        None,
        Any,
        Signed,
        Unsigned,
        Float,
        Array,
        Struct,
        Tuple,
        Lens,
        Function,
        Module
    }

    internal sealed class StringContext : IContext
    {
        public static readonly StringContext Instance = new ();

        public Storage? GetIdentifier(Compiler compiler, IdentifierExpression expression)
        {
            if (expression.Value == "Length")
            {
                var storage = compiler.CurrentFunction.AllocateStorage(CobType.U64);
                compiler.CurrentFunction.Body.EmitOA(
                    Opcode.GetField,
                    storage.Operand,
                    new []
                    {
                        compiler.BinOpLHS.Operand,
                        new Operand { Type = OperandType.ImmediateUnsigned, Value = 0 }
                    }
                );

                return storage;
            }

            return null;
        }

        public void SetIdentifier(Compiler compiler, IdentifierExpression expression)
        {
            
        }
    }
}