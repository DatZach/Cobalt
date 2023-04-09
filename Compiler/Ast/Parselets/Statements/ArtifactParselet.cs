using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class ArtifactParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var container = parser.Take(TokenType.Identifier);
            
            IReadOnlyList<string> containerParameters;
            if (parser.Match(TokenType.Add))
            {
                var lContainerParameters = new List<string>(4);
                while (parser.MatchAndTakeToken(TokenType.Add) != null)
                {
                    var parameter = parser.Take(TokenType.Identifier);
                    lContainerParameters.Add(parameter.Value);
                }

                containerParameters = lContainerParameters;
            }
            else
                containerParameters = Array.Empty<string>();

            var platform = parser.Take(TokenType.Identifier);
            
            var filename = parser.MatchAndTakeToken(TokenType.String);

            return new ArtifactExpression(
                token,
                container.Value,
                containerParameters,
                platform.Value,
                filename?.Value
            );
        }
    }
}
