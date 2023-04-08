using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class ReturnParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            Expression expression;
            if (parser.MatchAndTakeToken(TokenType.Semicolon) == null)
                expression = parser.ParseExpression();
            else
                expression = null;

            return new ReturnStatement(token, expression);
        }
    }
}
