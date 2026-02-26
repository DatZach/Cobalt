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
        private readonly List<TupleType> tupleTypes;
        private readonly List<StructType> structTypes;
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
            tupleTypes = new List<TupleType>();
            structTypes = new List<StructType>();
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

        public TupleType AllocateTupleType(string name)
        {
            var tupleType = new TupleType(name, this, compiler);
            tupleTypes.Add(tupleType);
            compiler.TupleTypes.Add(tupleType);

            return tupleType;
        }

        public TupleType? FindTupleType(string name)
        {
            return tupleTypes.FirstOrDefault(x => x.Name == name);
        }

        public StructType AllocateStructType(string name)
        {
            var structType = new StructType(name, this, compiler);
            structTypes.Add(structType);
            compiler.StructTypes.Add(structType);

            return structType;
        }

        public StructType? FindStructType(string name)
        {
            return structTypes.FirstOrDefault(x => x.Name == name);
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

        public ISymbol? FindSymbol(string name)
        {
            // GLOBAL
            Variable? global;
            if ((global = FindGlobal(name)) != null)
                return global;

            // FUNCTION
            Function? function;
            if ((function = FindFunction(name)) != null)
                return function;

            // MODULES
            Module? module;
            if ((module = FindModule(name)) != null)
                return module;

            // TUPLE TYPES
            TupleType? tupleType;
            if ((tupleType = FindTupleType(name)) != null)
                return tupleType;

            // STRUCT TYPES
            StructType? structType;
            if ((structType = FindStructType(name)) != null)
                return structType;

            return null;
        }

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

            // MODULES
            if (symbol is Module module)
            {
                return new Storage(
                    new CobType(eCobType.Module, tag: module),
                    Operand.None
                );
            }

            // TUPLE TYPES
            if (symbol is TupleType tupleType)
            {
                return new Storage(
                    new CobType(eCobType.Tuple, tag: tupleType),
                    Operand.None
                );
            }

            // STRUCT TYPES
            if (symbol is StructType structType)
            {
                return new Storage(
                    new CobType(eCobType.Struct, tag: structType),
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