using Compiler.Lexer;
using Compiler.Parser.Expressions;

namespace Compiler.Parser.Parselets
{
    internal sealed class FunctionParselet : IPrefixExpressionParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            parser.Take(TokenType.LeftParen);
            // TODO Parse parameters
            parser.Take(TokenType.RightParen);

            Expression? body;
            if (parser.Match(TokenType.LeftBrace))
                body = parser.ParseBlock(true);
            else
                body = null;

            return new FunctionExpression(
                token,
                Array.Empty<FunctionExpression.Parameter>(),
                body
            );
        }
    }
}
