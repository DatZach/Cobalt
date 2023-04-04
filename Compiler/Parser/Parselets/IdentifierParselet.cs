using Compiler.Lexer;
using Compiler.Parser.Expressions;

namespace Compiler.Parser.Parselets
{
    internal sealed class IdentifierParselet : IPrefixExpressionParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            return new IdentifierExpression(token);
        }
    }
}
