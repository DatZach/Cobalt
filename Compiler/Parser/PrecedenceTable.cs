namespace Compiler.Parser
{
    internal static class PrecedenceTable
    {
        public const int FunctionCall = 3;
        public const int Addition = 2;
        public const int Assignment = 1;
    }
}
