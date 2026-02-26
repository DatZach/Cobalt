namespace Compiler.CodeGeneration.Artifacts
{
    internal sealed class Artifact
    {
        public List<ArtifactDirective> ArtifactDirectives { get; }

        public List<Import> Imports { get; }

        public List<Export> Exports { get; }

        public List<Module> Modules { get; }

        public List<TraitType> TraitTypes { get; }

        public List<TupleType> TupleTypes { get; }

        public List<StructType> StructTypes { get; }

        public List<Function> Functions { get; }

        public List<Variable> Globals { get; }

        public List<CobType> Types { get; }

        public Function? EntryFunction { get; set; }

        public Artifact()
        {
            ArtifactDirectives = new List<ArtifactDirective>();
            Imports = new List<Import>();
            Exports = new List<Export>();
            Modules = new List<Module>();
            TraitTypes = new List<TraitType>();
            TupleTypes = new List<TupleType>();
            StructTypes = new List<StructType>();
            Functions = new List<Function>();
            Globals = new List<Variable>();
            Types = new List<CobType>();
        }
    }
}
