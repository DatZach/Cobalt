using Compiler.Ast.Expressions.Statements;

namespace Compiler.CodeGeneration
{
    internal static class ArtifactFactory
    {
        private readonly static Dictionary<string, ArtifactAssembler> platforms;

        static ArtifactFactory()
        {
            var asm = typeof(ArtifactFactory).Assembly;

            var types = asm.GetTypes().Where(myType =>
                myType.IsClass && !myType.IsAbstract && myType.IsSubclassOf(typeof(ArtifactAssembler)));
            foreach (var type in types)
            {
                var assembler = (ArtifactAssembler)Activator.CreateInstance(type);
                foreach (var platform in assembler.SupportedPlatforms)
                    platforms.Add(platform, assembler);
            }
        }

        public static void ProduceFrom(Compiler compiler)
        {
            foreach (var artifact in compiler.Artifacts)
            {
                if (!platforms.TryGetValue(artifact.Platform, out var platform))
                    throw new Exception($"Unsupported artifact platform '{artifact.Platform}'");

                var outputFilename = artifact.Filename
                                  ?? Path.ChangeExtension(Program.Config.EntrySourceFile, platform.DefaultExtension);
                platform.Assemble(compiler, artifact, outputFilename);
            }
        }
    }

    internal abstract class ArtifactAssembler
    {
        public abstract IReadOnlyList<string> SupportedPlatforms { get; }

        public abstract string DefaultExtension { get; }
        
        public abstract void Assemble(Compiler compiler, ArtifactExpression artifact, string outputFilename);
    }
}
