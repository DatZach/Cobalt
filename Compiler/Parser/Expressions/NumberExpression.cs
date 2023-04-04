using Compiler.Lexer;
using Compiler.Parser.Visitors;

namespace Compiler.Parser.Expressions
{
    internal sealed class NumberExpression : Expression
    {
        public long LongValue { get; }

        public NumberExpression(Token token, long longValue)
            : base(token)
        {
            LongValue = longValue;
        }

        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
