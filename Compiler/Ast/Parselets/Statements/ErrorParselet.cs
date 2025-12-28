using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal class ErrorParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var members = new List<string>();

            parser.Take(TokenType.LeftBrace);
            if (parser.MatchAndTakeToken(TokenType.RightBrace) == null)
            {
                do
                {
                    var member = parser.Take(TokenType.Identifier);
                    members.Add(member.Value);
                } while (parser.MatchAndTakeToken(TokenType.Comma) != null);

                parser.Take(TokenType.RightBrace);
            }

            return new ErrorStatement(token, members);
        }
    }
}
