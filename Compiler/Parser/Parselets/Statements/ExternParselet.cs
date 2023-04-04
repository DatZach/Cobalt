using Compiler.Lexer;
using Compiler.Parser.Expressions;
using Compiler.Parser.Expressions.Statements;

namespace Compiler.Parser.Parselets.Statements
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
