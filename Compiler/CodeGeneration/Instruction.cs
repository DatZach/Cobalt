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
            if (D != null && B != null) return $"{Opcode,-12}{A}, {B}, ({string.Join(", ", D)})";
            if (D != null) return $"{Opcode,-12}{A}, ({string.Join(", ", D)})";
            if (C != null) return $"{Opcode,-12}{A}, {B}, {C}";
            if (B != null) return $"{Opcode,-12}{A}, {B}";
            if (A != null) return $"{Opcode,-12}{A}";
            
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
            return Type switch
            {
                OperandType.None => "<none>",
                OperandType.ImmediateSigned8 => Value.ToString("D") + "_s8",
                OperandType.ImmediateSigned16 => Value.ToString("D") + "_s16",
                OperandType.ImmediateSigned32 => Value.ToString("D") + "_s32",
                OperandType.ImmediateSigned64 => Value.ToString("D") + "_s64",
                OperandType.ImmediateUnsigned8 => ((ulong)Value).ToString("D") + "_u8",
                OperandType.ImmediateUnsigned16 => ((ulong)Value).ToString("D") + "_u16",
                OperandType.ImmediateUnsigned32 => ((ulong)Value).ToString("D") + "_u32",
                OperandType.ImmediateUnsigned64 => ((ulong)Value).ToString("D") + "_u64",
                OperandType.ImmediateReal32 => BitConverter.Int64BitsToDouble(Value).ToString("F") + "_r32",
                OperandType.ImmediateReal64 => BitConverter.Int64BitsToDouble(Value).ToString("F") + "_r64",
                OperandType.Register or OperandType.Argument or OperandType.Local or OperandType.Global
                    or OperandType.Function
                    or OperandType.Type => Type.ToString()[..1].ToLowerInvariant() + Value.ToString("G"),
                OperandType.Label => $".label_{Value}",
                _ => throw new ArgumentOutOfRangeException()
            };
        }
        
        public static Operand ImmediateSigned(long value, int width = -1) => new()
        {
            Type = width switch
            {
                8 => OperandType.ImmediateSigned8,
                16 => OperandType.ImmediateSigned16,
                32 => OperandType.ImmediateSigned32,
                64 => OperandType.ImmediateSigned64,
                -1 => OperandType.ImmediateSigned64, // TODO Might not be right
                _ => throw new ArgumentOutOfRangeException(nameof(width))
            },
            Value = value
        };

        public static Operand ImmediateUnsigned(ulong value, int width = -1) => new()
        {
            Type = width switch
            {
                8 => OperandType.ImmediateUnsigned8,
                16 => OperandType.ImmediateUnsigned16,
                32 => OperandType.ImmediateUnsigned32,
                64 => OperandType.ImmediateUnsigned64,
                -1 => OperandType.ImmediateUnsigned64, // TODO Might not be right
                _ => throw new ArgumentOutOfRangeException(nameof(width))
            },
            Value = (long)value
        };
        public static Operand ImmediateUnsigned(int value, int width = -1) => ImmediateUnsigned((ulong)value, width);
        
        public static Operand ImmediateReal(double value, int width = -1) => new()
        {
            Type = width switch
            {
                32 => OperandType.ImmediateReal32,
                64 => OperandType.ImmediateReal64,
                -1 => OperandType.ImmediateReal64, // TODO Might not be right
                _ => throw new ArgumentOutOfRangeException(nameof(width))
            },
            Value = BitConverter.DoubleToInt64Bits(value)
        };
        
        public static Operand _Register(int value) => new() { Type = OperandType.Register, Value = value };
        public static Operand Argument(int value) => new() { Type = OperandType.Argument, Value = value };
        public static Operand Local(int value) => new() { Type = OperandType.Local, Value = value };
        public static Operand Global(int value) => new() { Type = OperandType.Global, Value = value };
        public static Operand Label(int value) => new() { Type = OperandType.Label, Value = value };
        public static Operand Function(int value) => new() { Type = OperandType.Function, Value = value };
        public static Operand _Type(long value) => new() { Type = OperandType.Type, Value = value };
    }

    public static class OperandUtility
    {
        public static bool IsImmediate(this OperandType type)
        {
            return type is OperandType.ImmediateSigned8 or OperandType.ImmediateSigned16
                        or OperandType.ImmediateSigned32 or OperandType.ImmediateSigned64
                        or OperandType.ImmediateUnsigned8 or OperandType.ImmediateUnsigned16
                        or OperandType.ImmediateUnsigned32 or OperandType.ImmediateUnsigned64
                        or OperandType.ImmediateReal32 or OperandType.ImmediateReal64;
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
        Peek,
        Poke,
        New,

        Call,
        CallVirt,
        Return,

        CmpEQ,
        CmpNEQ,
        CmpLT,
        CmpLTE,
        CmpGT,
        CmpGTE,
        CmpTyEQ,
        CmpTyNEQ,

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
        Not,

        Cast,

        PanicOnErr,
        SetPanicHandler
    }

    public enum OperandType : byte
    {
        None,
        Register,
        Argument,
        Local,
        Global,
        Label,
        Function,
        Type,

        ImmediateSigned8,
        ImmediateSigned16,
        ImmediateSigned32,
        ImmediateSigned64,

        ImmediateUnsigned8,
        ImmediateUnsigned16,
        ImmediateUnsigned32,
        ImmediateUnsigned64,

        ImmediateReal32,
        ImmediateReal64
    }
}
