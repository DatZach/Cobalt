using Compiler.Ast.Visitors;
using Compiler.Lexer;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class ReturnStatement : Expression
    {
        public Expression? Expression { get; }

        public ReturnStatement(Token token, Expression? expression)
            : base(token)
        {
            Expression = expression;
        }

        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
