namespace Compiler.Ast
{
    internal static class PrecedenceTable
    {
        public const int FunctionCall = 14;
        
        public const int Multiplication = 13;
        public const int Division = 13;
        public const int Modulo = 13;
        
        public const int Addition = 12;
        public const int Subtraction = 12;

        public const int BitLeftShift = 11;
        public const int BitRightShift = 11;

        public const int BitXor = 10;
        public const int BitAnd = 9;
        public const int BitOr = 8;

        public const int ConditionalExpression = 2; // ?

        public const int Assignment = 1;
    }
}
