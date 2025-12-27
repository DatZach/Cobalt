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

            var architecture = parser.Take(TokenType.Identifier);
            
            var filename = parser.MatchAndTakeToken(TokenType.String);

            IReadOnlyList<BinaryOperatorExpression>? containerParameters;
            if (parser.MatchAndTakeToken(TokenType.LeftBrace) != null)
            {
                var lAssignments = new List<BinaryOperatorExpression>();
                do
                {
                    var expr = parser.ParseExpression();
                    if (expr is not BinaryOperatorExpression boe || boe.Operator != TokenType.Assign)
                    {
                        parser.Messages.Add(Message.UnexpectedToken2, expr, "assignment", expr.Token);
                        continue;
                    }

                    lAssignments.Add(boe);

                } while (parser.MatchAndTakeToken(TokenType.Comma) != null);

                parser.Match(TokenType.RightBrace);

                containerParameters = lAssignments;
            }
            else
                containerParameters = null;

            return new ArtifactStatement(
                token,
                container.Value,
                container.Value,
                filename?.Value,
                containerParameters
            );
        }
    }
}
