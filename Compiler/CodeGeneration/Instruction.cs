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
        public static readonly Operand None = new() { Type = OperandType.None, Size = 0, Value = 0 };

        public static readonly Operand R0 = new() { Type = OperandType.Register, Size = 64, Value = 0 };

        public static readonly Operand This = new() { Type = OperandType.Argument, Value = 0 };

        public OperandType Type { get; init; }

        public int Size { get; init; }

        public long Value { get; init; }

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
                    return Type.ToString()[..1].ToLowerInvariant()
                           + Value.ToString("G")
                           + "."
                           + Size.ToString("G");
                case OperandType.Label:
                    return $".label_{Value}";
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
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

        Call,
        Return,

        Compare,
        JumpIfF,
        JumpIfT,
        JumpIfLT,
        JumpIfLTE,
        JumpIfGT,
        JumpIfGTE,
        Jump,
        
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
        Label
    }
}
