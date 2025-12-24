using Compiler.Ast.Expressions;
using System.Diagnostics;

namespace Compiler.CodeGeneration
{
    [DebuggerDisplay("Module '{Name}'")]
    internal sealed class Module : IContext
    {
        public string? Name { get; }

        public IContext? Parent { get; }

        public List<Module> Modules { get; } = new ();

        public List<Function> Functions { get; } = new (); // TODO Should this just be a name? An index to compiler.Functions?

        public List<TupleType> TupleTypes { get; } = new ();

        public List<StructType> StructTypes { get; } = new ();

        public Dictionary<string, CobVariable> Variables { get; } = new (); // TODO Should this just be a name? An index to compiler.Functions? These are globals

        public Function InitializerFunction { get; }
        
        public bool IsRoot => Parent == null;

        private readonly Compiler compiler;

        public Module(Compiler compiler, IContext? parent, string? name)
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
            var function = new Function(name, this, callingConvention, parameters, returnType);
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

        public Storage? GetIdentifier(Compiler compiler, IdentifierExpression expression)
        {
            var value = expression.Value;

            // GLOBAL
            int idx;
            if (Variables.TryGetValue(value, out var global)
            &&  (idx = compiler.FindGlobal(global)) != -1)
            {
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
            Function? function;
            if ((function = Functions.FirstOrDefault(x => x.Name == expression.Value)) != null
            &&  (idx = compiler.Functions.IndexOf(function)) != -1)
            {
                // TODO Implement
                //if (!Compiler.IsSymbolVisible(global))
                //{
                //    Compiler.Messages.Add(Message.CannotAccessPrivateSymbol, expression, expression.Value, Compiler.CurrentModule.Name ?? "(root)");
                //    return null;
                //}

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
            Module? module;
            if ((module = FindModule(value)) != null)
            {
                return new Storage(
                    null,
                    Operand.None,
                    new CobType(eCobType.Module, tag: module)
                );
            }

            // TUPLE TYPES
            TupleType? tupleType;
            if ((tupleType = FindTupleType(value)) != null)
            {
                return new Storage(
                    null,
                    Operand.None,
                    new CobType(eCobType.Tuple, tag: tupleType)
                );
            }

            // TUPLE TYPES
            StructType? structType;
            if ((structType = FindStructType(value)) != null)
            {
                return new Storage(
                    null,
                    Operand.None,
                    new CobType(eCobType.Struct, tag: structType)
                );
            }

            return null;
        }

        public CobVariable? SetIdentifier(Compiler compiler, IdentifierExpression expression)
        {
            var value = expression.Value;

            int idx;
            if (Variables.TryGetValue(value, out var global)
            &&  (idx = compiler.FindGlobal(global)) != -1)
            {
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
                return global;
            }

            return null;
        }
    }
}