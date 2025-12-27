namespace Compiler.CodeGeneration
{
    internal sealed record Instruction
    {
        public Opcode Opcode { get; init; }

        public Operand? A { get; init; }

        public Operand? B { get; init; }

        public Operand? C { get; init; }

        public IReadOnlyList<Operand>? D { get; init; }

        public override string ToString()
        {
            if (D != null && B != null) return $"{Opcode,-10}{A}, {B}, ({string.Join(", ", D)})";
            if (D != null) return $"{Opcode,-10}{A}, ({string.Join(", ", D)})";
            if (C != null) return $"{Opcode,-10}{A}, {B}, {C}";
            if (B != null) return $"{Opcode,-10}{A}, {B}";
            if (A != null) return $"{Opcode,-10}{A}";
            
            return Opcode.ToString();
        }
    }   

    public sealed record Operand
    {
        public static readonly Operand None = new() { Type = OperandType.None, Value = 0 };

        public static readonly Operand R0 = new() { Type = OperandType.Register, Value = 0 };

        public static readonly Operand This = new() { Type = OperandType.Argument, Value = 0 };

        public OperandType Type { get; private init; }

        public long Value { get; private init; }

        public override string ToString()
        {
            switch (Type)
            {
                case OperandType.None: return "<none>";
                case OperandType.ImmediateSigned:
                    return Value.ToString("D");
                case OperandType.ImmediateUnsigned:
                    return ((ulong)Value).ToString("D");
                case OperandType.ImmediateFloat:
                    return BitConverter.Int64BitsToDouble(Value).ToString("F");
                case OperandType.Register:
                case OperandType.Argument:
                case OperandType.Local:
                case OperandType.Global:
                case OperandType.Function:
                case OperandType.TupleType:
                    return Type.ToString()[..1].ToLowerInvariant()
                           + Value.ToString("G");
                case OperandType.Label:
                    return $".label_{Value}";
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        public static Operand ImmediateSigned(long value) => new() { Type = OperandType.ImmediateSigned, Value = value };
        public static Operand ImmediateUnsigned(ulong value) => new() { Type = OperandType.ImmediateUnsigned, Value = (long)value };
        public static Operand ImmediateUnsigned(int value) => new() { Type = OperandType.ImmediateUnsigned, Value = value };
        public static Operand ImmediateFloat(double value) => new() { Type = OperandType.ImmediateSigned, Value = BitConverter.DoubleToInt64Bits(value) };
        public static Operand _Register(int value) => new() { Type = OperandType.Register, Value = value };
        public static Operand Argument(int value) => new() { Type = OperandType.Argument, Value = value };
        public static Operand Local(int value) => new() { Type = OperandType.Local, Value = value };
        public static Operand Global(int value) => new() { Type = OperandType.Global, Value = value };
        public static Operand Label(int value) => new() { Type = OperandType.Label, Value = value };
        public static Operand Function(int value) => new() { Type = OperandType.Function, Value = value };
        public static Operand TupleType(int value) => new() { Type = OperandType.TupleType, Value = value };
        public static Operand StructType(int value) => new() { Type = OperandType.StructType, Value = value };
    }

    internal enum Opcode
    {
        None,

        Move,
        GetField,
        SetField,
        GetElem,
        SetElem,
        Lens,
        New,

        Call,
        Return,

        CmpEQ,
        CmpNEQ,
        CmpLT,
        CmpLTE,
        CmpGT,
        CmpGTE,

        JmpT,
        JmpF,
        Jmp,
        
        Add,
        Sub,
        Mul,
        Pow,
        Div,
        DivCeil,
        DivFloor,
        Rem,
        Mod,
        BitShl,
        BitShr,
        BitRol,
        BitRor,
        BitAnd,
        BitOr,
        BitXor,
        BitNot,
        CondAnd,
        CondOr,
        Neg,
        Not
    }

    public enum OperandType : byte
    {
        None,
        ImmediateSigned,
        ImmediateUnsigned,
        ImmediateFloat,
        Register,
        Argument,
        Local,
        Global,
        Label,
        Function,
        TupleType,
        StructType
    }
}
