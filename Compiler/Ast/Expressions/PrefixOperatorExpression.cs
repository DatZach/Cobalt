using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions
{
    internal sealed class PrefixOperatorExpression : Expression
    {
        public TokenType Operator => Token.Type;

        public Expression Right { get; }

        public PrefixOperatorExpression(Token token, Expression right)
            : base(token)
        {
            Right = right;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
