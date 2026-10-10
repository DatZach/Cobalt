using Compiler.Ast.Expressions;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets
{
    internal sealed class ArrayLiteralParselet : IPrefixExpressionParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            Expression? countExpression = null;
            var elements = new List<Expression>();

            if (!parser.Match(TokenType.RightSquare))
            {
                bool takeElements;
                countExpression = parser.ParseExpression();
                if (parser.MatchAndTakeToken(TokenType.Semicolon) == null)
                {
                    elements.Add(countExpression);
                    countExpression = null;
                    takeElements = parser.MatchAndTakeToken(TokenType.Comma) != null;
                }
                else
                    takeElements = true;

                if (takeElements)
                {
                    do
                    {
                        var element = parser.ParseExpression();
                        elements.Add(element);

                        if (!parser.Match(TokenType.RightSquare))
                            parser.Take(TokenType.Comma);
                    } while (!parser.IsEndOfStream && !parser.Match(TokenType.RightSquare));
                }
            }

            parser.Take(TokenType.RightSquare);

            return new ArrayLiteralExpression(token, null, countExpression, elements);
        }
    }
}
