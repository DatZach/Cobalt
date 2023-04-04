using Compiler.Lexer;
using Compiler.Parser.Visitors;

namespace Compiler.Parser.Expressions
{
    internal sealed class BinaryOperatorExpression : Expression
    {
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
            throw new NotImplementedException();
        }
    }
}
