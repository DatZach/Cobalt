using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class ForParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            Expression? conditional, expression;

            if (parser.MatchAndTakeToken(TokenType.LeftParen) != null)
            {
                conditional = parser.ParseStatement(false);
                expression = parser.MatchAndTakeToken(TokenType.Semicolon) != null
                           ? parser.ParseExpression()
                           : null;

                parser.Take(TokenType.RightParen);
            }
            else
            {
                conditional = null;
                expression = null;
            }

            var label = parser.MatchAndTakeToken(TokenType.At) != null ? parser.Take(TokenType.Identifier) : null;

            var body = parser.ParseBlock();

            return new ForStatement(token, conditional, expression, label, body);
        }
    }
}
