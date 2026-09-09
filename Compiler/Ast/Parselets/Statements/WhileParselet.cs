using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class WhileParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            Expression? exprA, exprB;
            if (parser.MatchAndTakeToken(TokenType.LeftParen) != null)
            {
                exprA = parser.Match(TokenType.Var) ? parser.ParseStatement(false) : parser.ParseExpression();
                exprB = parser.MatchAndTakeToken(TokenType.Semicolon) != null ? parser.ParseExpression() : null;
                parser.Take(TokenType.RightParen);

                if (exprB == null)
                {
                    exprB = exprA;
                    exprA = null;
                }
            }
            else
            {
                exprA = null;
                exprB = null;
            }

            var tag = parser.MatchAndTakeToken(TokenType.At) != null ? parser.Take(TokenType.Identifier) : null;

            var body = parser.ParseBlock();

            if (Parser.IsStatementExpression(body))
                parser.Messages.Add(Message.CannotNakedNestStatement, body);

            return new WhileStatement(token, exprA, exprB, tag, body);
        }
    }
}
