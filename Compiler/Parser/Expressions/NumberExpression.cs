using Compiler.Lexer;
using Compiler.Parser.Visitors;

namespace Compiler.Parser.Expressions
{
    internal sealed class NumberExpression : Expression
    {
        public NumberExpression(Token token)
            : base(token)
        {

        }

        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            throw new NotImplementedException();
        }
    }
}
