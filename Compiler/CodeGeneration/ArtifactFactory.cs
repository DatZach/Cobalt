using System.Diagnostics;
using Compiler.CodeGeneration.Artifacts;

namespace Compiler.CodeGeneration
{
    internal static class ArtifactFactory
    {
        private static readonly Dictionary<string, ArtifactAssembler> architectures;

        static ArtifactFactory()
        {
            architectures = new Dictionary<string, ArtifactAssembler>();

            var asm = typeof(ArtifactFactory).Assembly;

            var types = asm.GetTypes().Where(myType =>
               myType.IsClass && !myType.IsAbstract && myType.IsSubclassOf(typeof(ArtifactAssembler)));
            foreach (var type in types)
            {
                var assembler = (ArtifactAssembler)Activator.CreateInstance(type);
                foreach (var architecture in assembler.SupportedArchitectures)
                    architectures.Add(architecture, assembler);
            }
        }

        public static void Assemble(Artifact artifact)
        {
            try
            {
                stopwatch.Start();
                foreach (var directive in artifact.ArtifactDirectives)
                {
                    if (!architectures.TryGetValue(directive.Architecture, out var architecture))
                        throw new Exception($"Unsupported artifact architecture '{directive.Architecture}'");

                    var outputFilename = directive.Filename != null
                                       ? Path.Combine(Program.SourceDirectory, directive.Filename)
                                       : Path.ChangeExtension(Program.Config.EntrySourceFilePath, architecture.DefaultExtension);

                    architecture.Assemble(artifact, directive, outputFilename);
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
        public abstract IReadOnlyList<string> SupportedArchitectures { get; }

        public abstract string DefaultExtension { get; }
        
        public abstract void Assemble(Artifact artifact, ArtifactDirective directive, string outputFilename);
    }
}
