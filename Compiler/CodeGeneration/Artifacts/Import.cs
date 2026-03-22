namespace Compiler.CodeGeneration.Artifacts
{
    internal sealed record Import
    {
        public string Library { get; init; }

        public string? SymbolName { get; init; }

        public Function? Function { get; init; }
    }
}
