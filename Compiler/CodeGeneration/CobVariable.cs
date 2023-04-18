namespace Compiler.CodeGeneration
{
    internal sealed record CobVariable
    {
        public string Name { get; set; } // HACK set

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
        public readonly static CobType String = eCobType.String;
        public readonly static CobType Int = new (eCobType.Signed, 32); // TODO Technically should be machine-width

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

        public static implicit operator CobType(eCobType type)
        {
            return new CobType(type, 32);
        }
        
        public override int GetHashCode()
        {
            return HashCode.Combine((int)Type, Size, ElementType, Function);
        }

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

            if (left.Type == eCobType.Array && right == eCobType.String)
                return true;
            if (right == eCobType.Array && left.Type == eCobType.String)
                return true;

            return left.Type == right;
        }

        public static CobType FromString(string? typeName)
        {
            if (typeName == null || typeName.Length < 2)
                return None;

            if (typeName[0] == 's' && char.IsDigit(typeName[1]))
                return new CobType(eCobType.Signed, int.Parse(typeName[1..]));
            if (typeName[0] == 'u' && char.IsDigit(typeName[1]))
                return new CobType(eCobType.Unsigned, int.Parse(typeName[1..]));
            if (typeName[0] == 'f' && char.IsDigit(typeName[1]))
                return new CobType(eCobType.Float, int.Parse(typeName[1..]));
            if (typeName[0] == 'f' && typeName[1] == 'n')
                return new CobType(eCobType.Function, 0);
            if (typeName == "string")
                return new CobType(eCobType.String, 0);

            // TODO Arrays, Traits

            return None;
        }

        public override string ToString()
        {
            if (ElementType != null)
                return $"{Type}.{Size}[{ElementType}]";
            return $"{Type}.{Size}";
        }
    }

    internal enum eCobType
    {
        None,
        Signed,
        Unsigned,
        Float,
        Trait,
        Array,
        Function,
        String // TODO Eh?
    }
}