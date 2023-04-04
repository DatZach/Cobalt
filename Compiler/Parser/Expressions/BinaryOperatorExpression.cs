using Compiler.Lexer;
using Compiler.Parser.Visitors;

namespace Compiler.Parser.Expressions
{
    internal sealed class BinaryOperatorExpression : Expression
    {
        public TokenType Operator => Token.Type;

        public Expression Left { get; }

        public Expression Right { get; }

        public BinaryOperatorExpression(Token token, Expression left, Expression right)
            : base(token)
        {
            Left = left;
            Right = right;
        }

        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
