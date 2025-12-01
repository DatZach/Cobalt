using Compiler.Ast.Visitors;
using Compiler.CodeGeneration;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class TupleDefinitionExpression : Expression, IContext
    {
        public string Name { get; }

        public IReadOnlyList<FieldDefinition> Fields { get; }

        public IReadOnlyList<FunctionExpression> Functions { get; }

        public TupleDefinitionExpression(
            Token token,
            string name,
            IReadOnlyList<FieldDefinition> fields,
            IReadOnlyList<FunctionExpression> functions
        )
            : base(token)
        {
            Name = name;
            Fields = fields;
            Functions = functions;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }

        public Storage? ResolveIdentifier(CodeGeneration.Compiler compiler, IdentifierExpression expression)
        {
            // FIELD
            int idx; // TODO THIS IS SO BAD
            if ((idx = Fields.ToList().FindIndex(x => x.Name == expression.Value)) != -1)
            {
                var field = Fields[idx];
                var fieldType = field.Type;
                

                if (field.GetterExpression != null)
                {
                    return field.GetterExpression.Accept(compiler);
                }
                else
                {
                    var storage = compiler.CurrentFunction.AllocateStorage(fieldType);
                    compiler.CurrentFunction.Body.EmitOA(
                        Opcode.GetField,
                        storage.Operand,
                        new[]
                        {
                            compiler.BinOpLHS.Operand,
                            new Operand { Type = OperandType.ImmediateUnsigned, Value = idx }
                        }
                    );
                    return storage;
                }
            }

            // FUNCTION
            if (Functions.FirstOrDefault(x => x.Name == expression.Value) != null
            && (idx = compiler.FindGlobal(expression.Value)) != -1)
            {
                //if (!Compiler.IsSymbolVisible(global))
                //{
                //    Compiler.Messages.Add(Message.CannotAccessPrivateSymbol, expression, expression.Value, Compiler.CurrentModule.Name ?? "(root)");
                //    return null;
                //}

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

            return null;
        }
    }

    internal sealed record FieldDefinition(
        string Name,
        CobType Type,
        Expression? GetterExpression,
        Expression? SetterExpression
    );
}
