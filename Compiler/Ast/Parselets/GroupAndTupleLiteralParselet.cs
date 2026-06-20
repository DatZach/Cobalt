using Compiler.Lexer;
using Compiler.Ast.Expressions;

namespace Compiler.Ast.Parselets
{
    internal sealed class GroupAndTupleLiteralParselet : IPrefixExpressionParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            if (parser.Match(TokenType.RightParen)) // 0-ary Tuple
            {
                var endToken = parser.Take(TokenType.RightParen);
                return new TupleLiteralExpression(token, endToken, Array.Empty<Expression>());
            }
            else
            {
                var expression = parser.ParseExpression();
                if (parser.Match(TokenType.Comma)) // 2+ary Tuple
                {
                    var expressions = new List<Expression> { expression };
                    do
                    {
                        parser.Take(TokenType.Comma);
                        expression = parser.ParseExpression();
                        expressions.Add(expression);
                    } while (parser.Match(TokenType.Comma));

                    var endToken = parser.Take(TokenType.RightParen);
                    return new TupleLiteralExpression(token, endToken, expressions);
                }
                else // Group
                {
                    parser.Take(TokenType.RightParen);
                    return expression;
                }
            }
        }
    }
}
