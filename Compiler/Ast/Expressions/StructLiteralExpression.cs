using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions
{
    internal sealed class StructLiteralExpression : Expression
    {
        public Expression StructTypeExpression { get; }

        public IReadOnlyList<BinaryOperatorExpression>? Assignments { get; }

        public override Token StartToken => StructTypeExpression.StartToken;

        public override Token EndToken { get; }

        public StructLiteralExpression(
            Token token,
            Token endToken,
            Expression structTypeExpression,
            IReadOnlyList<BinaryOperatorExpression>? assignments
        )
            : base(token)
        {
            EndToken = endToken;
            StructTypeExpression = structTypeExpression;
            Assignments = assignments;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
