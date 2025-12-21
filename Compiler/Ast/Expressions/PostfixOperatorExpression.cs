using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions
{
    internal sealed class PostfixOperatorExpression : Expression
    {
        public TokenType Operator => Token.Type;

        public Expression Left { get; }

        public PostfixOperatorExpression(Token token, Expression left)
            : base(token)
        {
            Left = left;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
