using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class DeferParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var expr = parser.ParseBlock();

            return new DeferStatement(token, expr);
        }
    }
}