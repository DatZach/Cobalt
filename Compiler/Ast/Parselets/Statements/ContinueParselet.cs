using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class ContinueParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            return new ContinueStatement(token);
        }
    }
}
