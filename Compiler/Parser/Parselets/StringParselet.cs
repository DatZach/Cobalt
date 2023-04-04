using Compiler.Lexer;
using Compiler.Parser.Expressions;

namespace Compiler.Parser.Parselets
{
    internal sealed class StringParselet : IPrefixExpressionParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            return new StringExpression(token);
        }
    }
}
