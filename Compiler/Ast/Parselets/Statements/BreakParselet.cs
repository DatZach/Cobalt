using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class BreakParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var label = parser.MatchAndTakeToken(TokenType.At) != null ? parser.Take(TokenType.Identifier) : null;

            return new BreakStatement(token, label);
        }
    }
}
