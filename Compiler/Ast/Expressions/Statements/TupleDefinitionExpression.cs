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

        public IReadOnlyList<Expression> Functions { get; }

        public TupleDefinitionExpression(
            Token token,
            string name,
            IReadOnlyList<FieldDefinition> fields,
            IReadOnlyList<Expression> functions
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
            int idx; // TODO THIS IS SO BAD
            if ((idx = Fields.ToList().FindIndex(x => x.Name == expression.Value)) != -1)
            {
                var fieldType = Fields[idx].Type;
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
