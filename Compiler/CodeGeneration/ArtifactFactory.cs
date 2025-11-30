using System.Diagnostics;
using Compiler.Ast.Expressions.Statements;

namespace Compiler.CodeGeneration
{
    internal static class ArtifactFactory
    {
        private static readonly Dictionary<string, ArtifactAssembler> platforms;

        static ArtifactFactory()
        {
            platforms = new Dictionary<string, ArtifactAssembler>();

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

        public static void Assemble(Compiler compiler)
        {
            try
            {
                stopwatch.Start();
                foreach (var artifact in compiler.Artifacts)
                {
                    if (!platforms.TryGetValue(artifact.Platform, out var platform))
                        throw new Exception($"Unsupported artifact platform '{artifact.Platform}'");

                    string outputFilename;
                    if (artifact.Filename != null)
                    {
                        var sourceRoot = Path.GetDirectoryName(Program.Config.EntrySourceFile);
                        outputFilename = Path.Combine(sourceRoot, artifact.Filename);
                    }
                    else
                        outputFilename = Path.ChangeExtension(Program.Config.EntrySourceFile, platform.DefaultExtension);

                    platform.Assemble(compiler, artifact, outputFilename);
                }
            }
            finally
            {
                stopwatch.Stop();
            }
        }

        public static long TotalMilliseconds => stopwatch.ElapsedMilliseconds;
        private static readonly Stopwatch stopwatch = new ();
    }

    internal abstract class ArtifactAssembler
    {
        public abstract IReadOnlyList<string> SupportedPlatforms { get; }

        public abstract string DefaultExtension { get; }
        
        public abstract void Assemble(Compiler compiler, ArtifactExpression artifact, string outputFilename);
    }
}
