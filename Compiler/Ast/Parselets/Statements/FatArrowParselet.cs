using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class FatArrowParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var expression = parser.ParseExpression();
            return new FatArrowExpression(token, expression);
        }
    }
}
