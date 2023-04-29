namespace Compiler.CodeGeneration
{
    internal sealed record CobVariable
    {
        public string Name { get; set; } // TODO HACK set

        public CobType Type { get; }

        public byte[]? Data { get; set; } // TODO HACK AAAAAA???

        public long Value { get; set; }

        public CobVariable(string name, CobType type)
        {
            Name = name;
            Type = type;
        }

        public CobVariable(string name, CobType type, long value)
        {
            Name = name;
            Type = type;
            Value = value;
        }

        public override string ToString()
        {
            return $"{Name,-20}{Type} = {Data}";
        }
    }

    internal sealed record CobType
    {
        public readonly static CobType None = eCobType.None;
        public readonly static CobType Int = new (eCobType.Signed, 64); // TODO Technically should be machine-width
        public readonly static CobType UInt = new (eCobType.Unsigned, 64); // TODO Ditto
        public readonly static CobType U8 = new (eCobType.Unsigned, 8);
        public readonly static CobType Char = new (eCobType.Unsigned, 8) { AliasName = "char" };
        public readonly static CobType String = new (eCobType.Array, Char) { AliasName = "string" };

        public string? AliasName { get; init; }

        public eCobType Type { get; }

        public int Size { get; }

        public CobType? ElementType { get; }

        public Function? Function { get; }

        public CobType(eCobType type, int size, CobType? elementType = null, Function? function = null)
        {
            Type = type;
            Size = size;
            ElementType = elementType;
            Function = function;
        }

        public CobType(eCobType type, CobType elementType)
        {
            // TODO Validate "Container" types?
            Type = type;
            ElementType = elementType;
        }

        public static implicit operator CobType(eCobType type)
        {
            return new CobType(type, 32);
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
                    return new CobType(eCobType.Function, 0);
                if (typeName.StartsWith("int"))
                    return Int;
                if (typeName.StartsWith("uint"))
                    return UInt;
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

        public override string ToString()
        {
            if (AliasName != null)
                return AliasName;
            
            if (ElementType != null)
                return $"{Type}[{ElementType}]";

            return $"{Type}.{Size}";
        }

        public static bool IsCastable(CobType lhsType, CobType rhsType)
        {
            if (lhsType == rhsType)
                return true;

            if (lhsType.Type is eCobType.Unsigned or eCobType.Signed or eCobType.Float
            &&  rhsType.Type is eCobType.Unsigned or eCobType.Signed or eCobType.Float)
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
        Signed,
        Unsigned,
        Float,
        Array,
        Struct,
        Reference,
        Lens,
        Function
    }
}