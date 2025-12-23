using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class ModuleParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var nameToken = parser.MatchAndTakeToken(TokenType.Identifier);
            var block = parser.Match(TokenType.LeftBrace) ? parser.ParseBlock(true) : null;

            return new ModuleStatement(token, nameToken?.Value, block);
        }
    }
}
