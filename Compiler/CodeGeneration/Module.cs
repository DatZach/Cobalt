using Compiler.Ast;
using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;

namespace Compiler.CodeGeneration
{
    internal sealed class Module : IContext
    {
        public Compiler Compiler { get; }

        public string? Name { get; init; }

        public List<Function> Functions { get; } = new ();

        public Dictionary<string, CobVariable> Variables { get; } = new ();

        public List<TupleDefinitionExpression> TupleTypes { get; } = new ();

        public Function InitializerFunction { get; }

        public Module(Compiler compiler, string? name)
        {
            Compiler = compiler;
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

            return function;
        }

        public Storage? GetIdentifier(Compiler compiler, IdentifierExpression expression)
        {
            var value = expression.Value;

            int idx;
            if (Variables.TryGetValue(value, out var global)
            &&  (idx = Compiler.FindGlobal(global)) != -1)
            {
                if (!Compiler.IsSymbolVisible(global))
                {
                    Compiler.Messages.Add(Message.CannotAccessPrivateSymbol, expression, expression.Value, Compiler.CurrentModule.Name ?? "(root)");
                    return null;
                }

                var type = Compiler.Globals[idx];
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

            // MODULES
            // TODO Compiler.Modules should be nested here
            Module? module;
            if ((module = Compiler.Modules.FirstOrDefault(x => x.Name == value)) != null)
            {
                return new Storage(
                    null,
                    Operand.None,
                    new CobType(eCobType.Module, tag: module)
                );
            }

            // TUPLE TYPES
            TupleDefinitionExpression? tupleType;
            if ((tupleType = TupleTypes.FirstOrDefault(x => x.Name == value)) != null)
            {
                return new Storage(
                    null,
                    Operand.None,
                    new CobType(eCobType.Tuple, tag: tupleType)
                );
            }

            return null;
        }

        public void SetIdentifier(CodeGeneration.Compiler compiler, IdentifierExpression expression)
        {

        }
    }
}