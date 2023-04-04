using Compiler.Lexer;
using Compiler.Parser.Visitors;

namespace Compiler.Parser.Expressions
{
    internal sealed class StringExpression : Expression
    {
        public string Value => Token.Value!;

        public StringExpression(Token token)
            : base(token)
        {

        }

        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
