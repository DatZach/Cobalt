using System.Diagnostics;

namespace Compiler.CodeGeneration
{
    [DebuggerDisplay("Module '{Name}'")]
    internal sealed class Module : IScopeContext, ISymbol
    {
        public string Name { get; }

        public IScopeContext? Parent { get; }

        public Function InitializerFunction { get; }

        public bool IsRoot => Parent == null;

        private readonly List<Module> modules;
        private readonly List<TupleType> tupleTypes;
        private readonly List<StructType> structTypes;
        private readonly List<Function> functions;
        private readonly List<CobVariable> globals;
        private readonly Compiler compiler;

        public Module(string? name, IScopeContext? parent, Compiler compiler)
        {
            Name = name ?? "$Root";
            Parent = parent;
            this.compiler = compiler;

            modules = new List<Module>();
            tupleTypes = new List<TupleType>();
            structTypes = new List<StructType>();
            functions = new List<Function>();
            globals = new List<CobVariable>();

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

        public TupleType AllocateTupleType(string name)
        {
            var tupleType = new TupleType(name, this, compiler);
            tupleTypes.Add(tupleType);

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
            var function = new Function(name, compiler, this, callingConvention, parameters, returnType);
            functions.Add(function);
            compiler.Functions.Add(function);

            return function;
        }

        public Function? FindFunction(string name)
        {
            return functions.FirstOrDefault(x => x.Name == name);
        }

        public CobVariable AllocateGlobal(string name, CobType type, bool mutable)
        {
            // TODO Bit of a hack this isn't really a field but we need the visiblity rules and maybe we actually
            //      want to allow for global module variables to be fields??
            var global = new CobField(this, name, type, null, null);
            globals.Add(global);
            compiler.Globals.Add(global);

            return global;
        }

        public CobVariable? FindGlobal(string name)
        {
            return globals.FirstOrDefault(x => x.Name == name);
        }

        public ISymbol? FindSymbol(string name)
        {
            // GLOBAL
            CobVariable? global;
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

            // TUPLE TYPES
            StructType? structType;
            if ((structType = FindStructType(name)) != null)
                return structType;

            return null;
        }

        public Storage? EmitGetForSymbol(ISymbol symbol)
        {
            // GLOBAL
            if (symbol is CobVariable global)
            {
                var idx = compiler.Globals.IndexOf(global);
                var type = compiler.Globals[idx];
                return new Storage(
                    null,
                    new Operand
                    {
                        Type = OperandType.Global,
                        Value = idx,
                        Size = type.Type.Size
                    },
                    type.Type
                );
            }

            // FUNCTION
            if (symbol is Function function)
            {
                var idx = compiler.Functions.IndexOf(function);
                return new Storage(
                    null,
                    new Operand
                    {
                        Type = OperandType.Function,
                        Value = idx
                    },
                    new CobType(eCobType.Function, tag: compiler.Functions[idx])
                );
            }

            // MODULES
            if (symbol is Module module)
            {
                return new Storage(
                    null,
                    Operand.None,
                    new CobType(eCobType.Module, tag: module)
                );
            }

            // TUPLE TYPES
            if (symbol is TupleType tupleType)
            {
                return new Storage(
                    null,
                    Operand.None,
                    new CobType(eCobType.Tuple, tag: tupleType)
                );
            }

            // TUPLE TYPES
            if (symbol is StructType structType)
            {
                return new Storage(
                    null,
                    Operand.None,
                    new CobType(eCobType.Struct, tag: structType)
                );
            }

            return null;
        }

        public bool EmitSetForSymbol(ISymbol symbol)
        {
            if (symbol is CobVariable global)
            {
                var idx = compiler.Globals.IndexOf(global);
                compiler.CurrentFunction.Body.Emit(
                    Opcode.Move,
                    new Operand
                    {
                        Type = OperandType.Global,
                        Value = idx,
                        Size = global.Type.Size
                    },
                    compiler.AssignmentRHS.Operand
                );
                return true;
            }

            return false;
        }

        public bool IsVisibleTo(IScopeContext? context) => Compiler.IsSymbolVisible(context, Parent, Name);
    }
}