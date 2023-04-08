using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class ArtifactParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var targetPlatform = parser.Take(TokenType.Identifier);
            var filename = parser.MatchAndTakeToken(TokenType.String);

            return new ArtifactExpression(token, targetPlatform.Value, filename?.Value);
        }
    }
}
