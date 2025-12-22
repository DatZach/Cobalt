using Compiler.Ast.Expressions;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets
{
    internal sealed class StructLiteralParselet : IInfixExpressionParselet
    {
        public int Precedence => PrecedenceTable.StructInitializer;

        public Expression Parse(Parser parser, Expression left, Token token)
        {
            IReadOnlyList<BinaryOperatorExpression>? assignments;
            if (!parser.Match(TokenType.RightBrace))
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

                assignments = lAssignments;
            }
            else
                assignments = null;

            var endToken = parser.Take(TokenType.RightBrace);

            return new StructLiteralExpression(token, endToken, left, assignments);
        }
    }
}
