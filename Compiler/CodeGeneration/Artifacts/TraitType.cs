namespace Compiler.CodeGeneration.Artifacts
{
    internal sealed class TraitType : IScopeContext, ISymbol
    {
        public string Name { get; }

        public IScopeContext? Parent { get; }

        public IReadOnlyList<Function> Functions => functions;

        private readonly List<Field> fields;
        private readonly List<Function> functions;

        private readonly Compiler compiler;

        public TraitType(string name, IScopeContext parent, Compiler compiler)
        {
            Name = name;
            Parent = parent;
            this.compiler = compiler;
            
            fields = new List<Field>(4);
            functions = new List<Function>();
        }

        public Function AllocateFunction(
            string name,
            IReadOnlyList<Function.Parameter> parameters,
            CobType returnType
        ) {
            var lParameters = new List<Function.Parameter>();
            lParameters.Add(new Function.Parameter("this", new CobType(eCobType.Trait, tag: this), false));
            lParameters.AddRange(parameters);

            var function = new Function(name, this, compiler, CallingConvention.ThisCall, lParameters, returnType);
            functions.Add(function);
            compiler.Functions.Add(function);

            return function;
        }

        public Function? FindFunction(string name)
        {
            return functions.FirstOrDefault(x => x.Name == name);
        }

        public FunctionCandidates? FindFunctionCandidates(string name)
        {
            var candidates = functions.Where(x => x.Name == name).ToList();
            return candidates.Count > 0
                ? new FunctionCandidates(candidates)
                : null;
        }

        public ISymbol? FindSymbol(string name)
        {
            FunctionCandidates? candidates;
            if ((candidates = FindFunctionCandidates(name)) != null)
                return candidates;

            return null;
        }

        public Storage? EmitGetForSymbol(ISymbol symbol)
        {
            if (symbol is Function function)
            {
                var idx = compiler.Functions.IndexOf(function);
                return new Storage(
                    new CobType(eCobType.Function, tag: compiler.Functions[idx]),
                    Operand.Function(idx)
                );
            }

            if (symbol is FunctionCandidates candidates)
            {
                return new Storage(
                    new CobType(eCobType.Function, tag: candidates),
                    Operand.None
                );
            }

            return null;
        }

        public bool EmitSetForSymbol(ISymbol symbol)
        {
            throw new NotImplementedException();
        }

        public bool IsVisibleTo(IScopeContext context) => Compiler.IsSymbolVisible(context, Parent, Name);

        public override string ToString() => Name;
    }
}
