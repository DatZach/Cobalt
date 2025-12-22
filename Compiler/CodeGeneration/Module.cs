using Compiler.Ast;
using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Lexer;

namespace Compiler.CodeGeneration
{
    internal sealed class Module : IContext
    {
        public Compiler Compiler { get; }

        public string? Name { get; init; }

        public List<Function> Functions { get; } = new ();

        public Dictionary<string, CobVariable> Variables { get; } = new ();

        public List<TupleDefinitionExpression> TupleTypes { get; } = new ();

        public List<StructDefinitionExpression> StructTypes { get; } = new ();

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

            // TUPLE TYPES
            StructDefinitionExpression? structType;
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
            &&  (idx = Compiler.FindGlobal(global)) != -1)
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

        public void ForwardDeclare(Compiler compiler, Expression expression)
        {
            if (expression is FatArrowExpression fae)
            {
                ForwardDeclare(compiler, fae.Expression);
            }
            else if (expression is BlockExpression be)
            {
                foreach (var expr in be.Expressions)
                    ForwardDeclare(compiler, expr);
            }
            else if (expression is FunctionExpression fe)
            {
                CallingConvention callingConvention;
                IReadOnlyList<Function.Parameter> parameters;
                if (compiler.CurrentContext is TupleDefinitionExpression)
                {
                    callingConvention = CallingConvention.ThisCall;
                    var lParameters = new List<Function.Parameter>(fe.Parameters);
                    lParameters.Insert(0, new Function.Parameter("this", new CobType(eCobType.Tuple, tag: compiler.CurrentContext), false));
                    parameters = lParameters;
                }
                else
                {
                    callingConvention = fe.CallingConvention;
                    parameters = fe.Parameters;
                }

                var function = AllocateFunction(
                    fe.Name,
                    callingConvention,
                    parameters,
                    fe.ReturnType
                );

                if (!fe.IsAnonymous)
                {
                    var type = new CobType(eCobType.Function, tag: function);
                    compiler.AllocateGlobal(new CobVariable(fe.Name, type, false));
                }
            }
            else if (expression is VarExpression ve)
            {
                if (compiler.CurrentFunction != compiler.CurrentModule.InitializerFunction)
                    return;

                foreach (var decl in ve.Declarations)
                {
                    var mutable = ve.Type == TokenType.Var;
                    var variable = new CobVariable(decl.Name, CobType.None, mutable);
                    compiler.AllocateGlobal(variable);
                }
            }
            else if (expression is TupleDefinitionExpression tde)
            {
                TupleTypes.Add(tde);
                foreach (var expr in tde.Functions)
                    ForwardDeclare(compiler, expr);
            }
            else if (expression is StructDefinitionExpression sde)
            {
                StructTypes.Add(sde);
                foreach (var expr in sde.Functions)
                    ForwardDeclare(compiler, expr);
            }
        }
    }
}