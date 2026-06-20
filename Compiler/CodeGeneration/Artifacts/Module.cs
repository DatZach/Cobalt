using System.Diagnostics;

namespace Compiler.CodeGeneration.Artifacts
{
    [DebuggerDisplay("Module '{Name}'")]
    internal sealed class Module : IScopeContext, ISymbol
    {
        public string Name { get; }

        public IScopeContext? Parent { get; }

        public Function InitializerFunction { get; }

        public bool IsRoot => Parent == null;

        private readonly List<Module> modules;
        private readonly List<TraitType> traitTypes;
        private readonly List<RecordType> recordTypes;
        private readonly List<DistinctType> distinctTypes;
        private readonly List<Function> functions;
        private readonly List<Variable> globals;
        private readonly Compiler compiler;

        public Module(string? name, IScopeContext? parent, Compiler compiler)
        {
            Name = name ?? "$Root";
            Parent = parent;
            this.compiler = compiler;

            modules = new List<Module>();
            traitTypes = new List<TraitType>();
            recordTypes = new List<RecordType>();
            distinctTypes = new List<DistinctType>();
            functions = new List<Function>();
            globals = new List<Variable>();

            InitializerFunction = AllocateFunction(
                "$Initializer",
                CallingConvention.CCall,
                Array.Empty<Function.Parameter>(),
                CobType.None
            );
        }

        public Module? FindModule(string? name)
        {
            return modules.FirstOrDefault(x => x.Name == name);
        }

        public Module FindOrAllocateModule(string? name)
        {
            var module = modules.FirstOrDefault(x => x.Name == name);
            if (module != null)
                return module;

            module = new Module(name, this, compiler);
            modules.Add(module);
            compiler.Modules.Add(module);

            return module;
        }

        public TraitType? AllocateTraitType(string name)
        {
            var traitType = new TraitType(name, this, compiler);
            traitTypes.Add(traitType);
            compiler.TraitTypes.Add(traitType);

            return traitType;
        }

        public TraitType? FindTraitType(string name)
        {
            return traitTypes.FirstOrDefault(x => x.Name == name);
        }

        public RecordType AllocateRecordType(string name, eRecordType type)
        {
            var recordType = new RecordType(name, type, this, compiler);
            recordTypes.Add(recordType);
            compiler.RecordTypes.Add(recordType);

            return recordType;
        }

        public RecordType? FindRecordType(string name)
        {
            return recordTypes.FirstOrDefault(x => x.Name == name);
        }

        public DistinctType AllocateDistinctType(string name, CobType subType)
        {
            var distinctType = new DistinctType(name, subType);
            distinctTypes.Add(distinctType);

            return distinctType;
        }

        public DistinctType? FindDistinctType(string name)
        {
            return distinctTypes.FirstOrDefault(x => x.Name == name);
        }

        public Function AllocateFunction(
            string name,
            CallingConvention callingConvention,
            IReadOnlyList<Function.Parameter> parameters,
            CobType returnType
        ) {
            var function = new Function(name, this, compiler, callingConvention, parameters, returnType);
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

        public Variable AllocateGlobal(string name, CobType type, bool mutable)
        {
            // TODO Bit of a hack this isn't really a field but we need the visiblity rules and maybe we actually
            //      want to allow for global module variables to be fields??
            var global = new Field(this, name, type, null, null);
            globals.Add(global);
            compiler.Globals.Add(global);

            return global;
        }

        public Variable? FindGlobal(string name)
        {
            return globals.FirstOrDefault(x => x.Name == name);
        }

        // TODO FindExplicitSymbol
        public ISymbol? FindSymbol(string name)
        {
            // GLOBAL
            Variable? global;
            if ((global = FindGlobal(name)) != null)
                return global;

            // FUNCTION
            FunctionCandidates? function;
            if ((function = FindFunctionCandidates(name)) != null)
                return function;

            // MODULES
            Module? module;
            if ((module = FindModule(name)) != null)
                return module;

            // RECORD TYPES
            RecordType? tupleType;
            if ((tupleType = FindRecordType(name)) != null)
                return tupleType;

            return null;
        }

        //public ISymbol? FindImplicitSymbol(eCobType containerType, IReadOnlyList<CobType> fieldTypes)
        //{
        //    if (containerType == eCobType.Tuple)
        //    {
        //        return recordTypes.FirstOrDefault(
        //            x => x.Type == eRecordType.Tuple
        //              && x.IsFieldSignatureMatch(fieldTypes)
        //        );
        //    }
            
        //    return null;
        //}

        public Storage? EmitGetForSymbol(ISymbol symbol)
        {
            // GLOBAL
            if (symbol is Variable global)
            {
                var idx = compiler.Globals.IndexOf(global);
                var type = compiler.Globals[idx];
                return new Storage(
                    type.Type,
                    Operand.Global(idx)
                );
            }

            // FUNCTION
            if (symbol is Function function)
            {
                var idx = compiler.Functions.IndexOf(function);
                return new Storage(
                    new CobType(eCobType.Function, tag: compiler.Functions[idx]),
                    Operand.Function(idx)
                );
            }

            // FUNCTION CANDIDATES
            if (symbol is FunctionCandidates candidates)
            {
                return new Storage(
                    new CobType(eCobType.Function, tag: candidates),
                    Operand.None
                );
            }

            // MODULES
            if (symbol is Module module)
            {
                return new Storage(
                    new CobType(eCobType.Module, tag: module),
                    Operand.None
                );
            }

            // RECORD TYPES
            if (symbol is RecordType recordType)
            {
                return new Storage(
                    recordType.ThisType,
                    Operand.None
                );
            }

            return null;
        }

        public bool EmitSetForSymbol(ISymbol symbol)
        {
            if (symbol is Variable global)
            {
                var idx = compiler.Globals.IndexOf(global);
                compiler.CurrentFunction.Body.Emit(
                    Opcode.Move,
                    Operand.Global(idx),
                    compiler.AssignmentRHS.Operand
                );
                return true;
            }

            return false;
        }

        public bool IsVisibleTo(IScopeContext? context) => Compiler.IsSymbolVisible(context, Parent, Name);
    }
}