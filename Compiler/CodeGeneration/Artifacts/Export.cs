namespace Compiler.CodeGeneration.Artifacts
{
    internal sealed record Export
    {
        public Function Function { get; init; }
    }
}
