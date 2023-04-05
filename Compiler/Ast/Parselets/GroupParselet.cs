using Compiler.Lexer;
using Compiler.Ast.Expressions;

namespace Compiler.Ast.Parselets
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
