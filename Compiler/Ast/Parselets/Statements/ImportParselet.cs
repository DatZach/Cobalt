using Compiler.Lexer;
using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class ImportParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var libraryExpr = parser.Take(TokenType.Identifier);
            var symbolName = parser.MatchAndTakeToken(TokenType.Identifier);
            var functionSignature = symbolName != null ? parser.ParseExpression() : null;

            return new ImportExpression(
                token,
                libraryExpr.Value,
                symbolName?.Value,
                functionSignature as FunctionExpression
            );
        }
    }
}
