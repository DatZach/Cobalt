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

        }

        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            throw new NotImplementedException();
        }
    }
}
