
namespace Compiler.CodeGeneration
{
    internal sealed record Instruction
    {
        public Opcode Opcode { get; init; }

        public Operand? A { get; init; }

        public Operand? B { get; init; }

        public override string ToString()
        {
            return $"{Opcode,-10}{A?.ToString() ?? ""} {B?.ToString() ?? ""}";
        }
    }   

    public sealed record Operand
    {
        public OperandType Type { get; init; }

        public byte Size { get; init; }

        public long Value { get; init; }

        public override string ToString()
        {
            switch (Type)
            {
                case OperandType.ImmediateSigned:
                    return Value.ToString("D");
                case OperandType.ImmediateUnsigned:
                    return ((ulong)Value).ToString("D");
                case OperandType.ImmediateFloat:
                    return BitConverter.Int64BitsToDouble(Value).ToString("F");
                case OperandType.Register:
                case OperandType.Pointer:
                case OperandType.Local:
                case OperandType.Global:
                case OperandType.Function:
                    return Type.ToString()[..1].ToLowerInvariant()
                           + Value.ToString("G")
                           + "."
                           + Size.ToString("G");
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    internal enum Opcode
    {
        None,

        Call,
        Return,
        RestoreStack,
        Jump,

        Move,
        Push,
        Pop,
        
        BitShr,
        BitShl,
        BitAnd,
        BitXor,
        BitOr,

        Add,
        Sub,
        Mul,
        Div,
        Mod
    }

    public enum OperandType : byte
    {
        None,
        ImmediateSigned,
        ImmediateUnsigned,
        ImmediateFloat,
        Register,
        Pointer,
        Local,
        Global,
        Function
    }
}
