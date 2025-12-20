namespace Compiler.Ast
{
    internal static class PrecedenceTable
    {
        public const int ArrayIndexer = 17;

        public const int FunctionCall = 16;
        public const int StructInitializer = 16;
        public const int Dereference = 16;

        public const int Unary = 15;
        public const int Range = 15;

        public const int Exponent = 14;
        public const int Multiplication = 13;
        public const int Division = 13;
        public const int Modulo = 13;
        
        public const int Addition = 12;
        public const int Subtraction = 12;

        public const int BitLeftShift = 11;
        public const int BitRightShift = 11;
        public const int BitPack = 11;

        public const int BitXor = 10;
        public const int BitAnd = 9;
        public const int BitOr = 8;

        public new const int Equals = 7;
        public const int NotEquals = 7;
        public const int LessThanOrEqual = 7;
        public const int MoreThanOrEqual = 7;
        public const int LessThan = 7;
        public const int MoreThan = 7;
        
        public const int ConditionalAnd = 5;
        public const int ConditionalOr = 4;

        //public const int ConditionalExpression = 2; // ?

        public const int Assignment = 1;
    }
}
