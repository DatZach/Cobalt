using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class MachineParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var target = parser.Take(TokenType.Identifier);
            var source = parser.Take(TokenType.String);

            return new MachineStatement(token, target.Value, source.Value);
        }
    }
}
