using Compiler.Ast;
using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;

namespace Compiler.CodeGeneration
{
    internal sealed class Module : IContext
    {
        public string? Name { get; init; }

        public List<Function> Functions { get; } = new (); // TODO Should this just be a name? An index to compiler.Functions?

        public Dictionary<string, CobVariable> Variables { get; } = new (); // TODO Should this just be a name? An index to compiler.Functions? These are globals

        public List<TupleDeclStatement> TupleTypes { get; } = new ();

        public List<StructDeclStatement> StructTypes { get; } = new ();

        public Function InitializerFunction { get; }
        
        public bool IsRoot => Name == null;

        private readonly Compiler compiler;

        public Module(Compiler compiler, string? name)
        {
            this.compiler = compiler;
            Name = name;

            InitializerFunction = AllocateFunction(
                "$Initializer",
                CallingConvention.CCall,
                Array.Empty<Function.Parameter>(),
                CobType.None
            );
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

        public Storage? GetIdentifier(Compiler compiler, IdentifierExpression expression)
        {
            var value = expression.Value;

            // GLOBAL
            int idx;
            if (Variables.TryGetValue(value, out var global)
            &&  (idx = compiler.FindGlobal(global)) != -1)
            {
                if (!compiler.IsSymbolVisible(global))
                {
                    compiler.Messages.Add(Message.CannotAccessPrivateSymbol, expression, expression.Value, compiler.CurrentModule.Name ?? "(root)");
                    return null;
                }

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
            idx = compiler.Functions.FindIndex(x => x.Name == expression.Value); // TODO AllocateFunction + FindFunctionIndex()
            if (idx != -1)
            {
                // TODO Implement
                //if (!Compiler.IsSymbolVisible(global))
                //{
                //    Compiler.Messages.Add(Message.CannotAccessPrivateSymbol, expression, expression.Value, Compiler.CurrentModule.Name ?? "(root)");
                //    return null;
                //}

                var function = compiler.Functions[idx];
                return new Storage(
                    null,
                    new Operand
                    {
                        Type = OperandType.Function,
                        Value = idx
                    },
                    new CobType(eCobType.Function, tag: function)
                );
            }

            // MODULES
            // TODO Compiler.Modules should be nested here
            Module? module;
            if ((module = compiler.Modules.FirstOrDefault(x => x.Name == value)) != null)
            {
                return new Storage(
                    null,
                    Operand.None,
                    new CobType(eCobType.Module, tag: module)
                );
            }

            // TUPLE TYPES
            TupleDeclStatement? tupleType;
            if ((tupleType = TupleTypes.FirstOrDefault(x => x.Name == value)) != null)
            {
                return new Storage(
                    null,
                    Operand.None,
                    new CobType(eCobType.Tuple, tag: tupleType)
                );
            }

            // TUPLE TYPES
            StructDeclStatement? structType;
            if ((structType = StructTypes.FirstOrDefault(x => x.Name == value)) != null)
            {
                return new Storage(
                    null,
                    Operand.None,
                    new CobType(eCobType.Struct, tag: structType)
                );
            }

            return null;
        }

        public void SetIdentifier(Compiler compiler, IdentifierExpression expression)
        {
            var value = expression.Value;

            int idx;
            if (Variables.TryGetValue(value, out var global)
            &&  (idx = compiler.FindGlobal(global)) != -1)
            {
                compiler.ValidateVariableAccess(global, expression);
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
                return;
            }
        }
    }
}