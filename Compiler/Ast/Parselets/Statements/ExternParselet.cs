using Compiler.Lexer;
using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class ExternParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var libraryExpr = parser.Take(TokenType.Identifier);
            var symbolName = parser.Take(TokenType.Identifier);
            var functionSignature = parser.ParseExpression();

            return new ExternExpression(
                token,
                libraryExpr.Value!,
                symbolName.Value!,
                functionSignature as FunctionExpression
            );
        }
    }
}
