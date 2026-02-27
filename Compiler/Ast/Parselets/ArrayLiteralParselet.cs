using Compiler.Ast.Expressions;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets
{
    internal sealed class ArrayLiteralParselet : IPrefixExpressionParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var elements = new List<Expression>();

            if (parser.Match(TokenType.RightSquare))
                parser.Take(TokenType.RightSquare);
            else do
            {
                var element = parser.ParseExpression();
                elements.Add(element);

                if (!parser.Match(TokenType.RightSquare))
                    parser.Take(TokenType.Comma);
            } while (!parser.IsEndOfStream && !parser.Match(TokenType.RightSquare));

            parser.Take(TokenType.RightSquare);

            return new ArrayLiteralExpression(token, elements);
        }
    }
}
