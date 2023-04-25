using Compiler.Lexer;
using Compiler.Ast.Visitors;
using System.Diagnostics;

namespace Compiler.Ast.Expressions
{
    internal sealed class BinaryOperatorExpression : Expression
    {
        public TokenType Operator => Token.Type;

        public Expression Left { get; }

        public Expression Right { get; }

        public override Token StartToken => Left.StartToken;

        public override Token EndToken => Right.EndToken;

        public BinaryOperatorExpression(Token token, Expression left, Expression right)
            : base(token)
        {
            Left = left;
            Right = right;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
