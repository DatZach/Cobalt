using Compiler.Ast.Expressions;
using System.Diagnostics;

namespace Compiler.CodeGeneration
{
    [DebuggerDisplay("Module '{Name}'")]
    internal sealed class Module : IScopeContext, ISymbol
    {
        public string? Name { get; }

        public IScopeContext? Parent { get; }

        public List<Module> Modules { get; } = new ();

        public List<Function> Functions { get; } = new (); // TODO Should this just be a name? An index to compiler.Functions?

        public List<TupleType> TupleTypes { get; } = new ();

        public List<StructType> StructTypes { get; } = new ();

        public Dictionary<string, CobVariable> Variables { get; } = new (); // TODO Should this just be a name? An index to compiler.Functions? These are globals

        public Function InitializerFunction { get; }
        
        public bool IsRoot => Parent == null;

        private readonly Compiler compiler;

        public Module(Compiler compiler, IScopeContext? parent, string? name)
        {
            this.compiler = compiler;
            Parent = parent;
            Name = name;

            InitializerFunction = AllocateFunction(
                "$Initializer",
                CallingConvention.CCall,
                Array.Empty<Function.Parameter>(),
                CobType.None
            );
        }

        public Module? FindModule(string? name)
        {
            return Modules.FirstOrDefault(x => x.Name == name);
        }

        public Module FindOrAllocateModule(string? name)
        {
            var module = Modules.FirstOrDefault(x => x.Name == name);
            if (module != null)
                return module;

            module = new Module(compiler, this, name);
            Modules.Add(module);
            compiler.Modules.Add(module);

            return module;
        }

        public Function AllocateFunction(
            string name,
            CallingConvention callingConvention,
            IReadOnlyList<Function.Parameter> parameters,
            CobType returnType
        ) {
            var function = new Function(name, compiler, this, callingConvention, parameters, returnType);
            Functions.Add(function);
            compiler.Functions.Add(function);

            return function;
        }

        public TupleType? FindTupleType(string name)
        {
            return TupleTypes.FirstOrDefault(x => x.Name == name);
        }

        public StructType? FindStructType(string name)
        {
            return StructTypes.FirstOrDefault(x => x.Name == name);
        }

        public void AllocateGlobal(string name, CobType type, bool mutable)
        {
            // TODO Bit of a hack this isn't really a field but we need the visiblity rules and maybe we actually
            //      want to allow for global module variables to be fields??
            var variable = new CobField(this, name, type, null, null);
            compiler.AllocateGlobal(variable);
            Variables[variable.Name] = variable;
        }

        public ISymbol? FindSymbol(string name)
        {
            // GLOBAL
            if (Variables.TryGetValue(name, out var global))
                return global;

            // FUNCTION
            Function? function;
            if ((function = Functions.FirstOrDefault(x => x.Name == name)) != null)
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
                var idx = compiler.FindGlobal(global); // TODO Weird naming convention IndexOfGlobal is better
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

            // TODO AllocateFunction + FindFunctionIndex()
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
                var idx = compiler.FindGlobal(global);
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

        public bool IsVisibleTo(IScopeContext? context) => Compiler.StandardIsSymbolVisibleHeuristic(context, Parent, Name);
    }
}