using Compiler.Lexer;
using Compiler.Parser.Expressions;

namespace Compiler.Parser.Parselets
{
    internal sealed class GroupParselet : IPrefixExpressionParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var expression = parser.ParseExpression();
            parser.Take(TokenType.RightParen);

            return expression;
        }
    }
}
