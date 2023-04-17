
namespace Compiler.CodeGeneration
{
    internal sealed record Instruction
    {
        public Opcode Opcode { get; init; }

        public Operand? A { get; init; }

        public Operand? B { get; init; }

        public IReadOnlyList<Operand>? C { get; init; }

        public override string ToString()
        {
            var cStr = C != null ? string.Join(", ", C) : null;

            return $"{Opcode,-10}{A?.ToString() ?? ""} {B?.ToString() ?? ""} {cStr ?? ""}";
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
                case OperandType.Argument:
                case OperandType.Local:
                case OperandType.Global:
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

        Stash,
        Unstash,
        Move,

        Call,
        Return,
        RestoreStack, // TODO Remove
        Jump,

        
        Push, // TODO Remove
        Pop,  // TODO Remove
        
        Add,
        Sub,
        Mul,
        Div,
        Mod,
        BitShr,
        BitShl,
        BitAnd,
        BitXor,
        BitOr,
        BitNot,
        Negate,
        LogicalNot,
        LogicalAnd,
        LogicalOr
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
        Global
    }
}
