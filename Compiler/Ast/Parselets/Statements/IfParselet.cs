using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class IfParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var conditional = parser.ParseExpression();

            var then = parser.ParseBlock();

            var @else = parser.MatchAndTakeToken(TokenType.Else) != null ? parser.ParseBlock() : null;

            // TODO Disallow statements in naked blocks

            return new IfStatement(token, conditional, then, @else);
        }
    }
}
