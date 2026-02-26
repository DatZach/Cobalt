namespace Compiler.CodeGeneration.Artifacts
{
    internal sealed record ArtifactDirective
    {
        public string Container { get; init; }

        public string Architecture { get; init; }

        public string? Filename { get; init; }

        public IReadOnlyDictionary<string, Variable> ContainerConfiguration { get; init; }
    }
}
