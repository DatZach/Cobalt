using Compiler.Lexer;
using Compiler.Parser.Visitors;

namespace Compiler.Parser.Expressions
{
    internal sealed class BlockExpression : Expression
    {
        public IReadOnlyList<Expression> Expressions { get; }

        public BlockExpression(Token token, IReadOnlyList<Expression> expressions)
            : base(token)
        {
            Expressions = expressions;
        }

        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
