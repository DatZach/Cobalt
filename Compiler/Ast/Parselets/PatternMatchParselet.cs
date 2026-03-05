using Compiler.Ast.Expressions;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets
{
    internal sealed class PatternMatchParselet : IInfixExpressionParselet
    {
        public int Precedence => PrecedenceTable.Is;

        public Expression Parse(Parser parser, Expression left, Token token)
        {
            PatternMatchExpression.Pattern? rightSingle;
            List<PatternMatchExpression.Pattern>? rightMulti;

            // x :: int
            // x !: int
            // x :: int y
            // x :: { int y => y, _ => 0 }
            // x :: { 1 => 1234, _ => 4321 }
            // x :: { error => 0, _ => 1 }

            if (parser.MatchAndTakeToken(TokenType.LeftBrace) != null)
            {
                rightMulti = new List<PatternMatchExpression.Pattern>(4);
                rightSingle = null;

                do
                {
                    var pattern = ParsePattern();
                    rightMulti.Add(pattern);
                } while (parser.MatchAndTakeToken(TokenType.Comma) != null);

                parser.Take(TokenType.RightBrace);
            }
            else
            {
                rightSingle = ParsePattern();
                rightMulti = null;
            }

            return new PatternMatchExpression(token, left, rightSingle, rightMulti);

            PatternMatchExpression.Pattern ParsePattern()
            {
                Token? token = null;
                string? typeName = null;
                Expression? valueExpr = null;
                Expression? right = null;

                if (parser.Match(TokenType.Identifier) || parser.Match(TokenType.Error) || parser.Match(TokenType.Nil))
                {
                    token = parser.Peek();
                    typeName = parser.ParseTypeName();
                }

                if (!parser.Match(TokenType.FatArrow) && !parser.Match(TokenType.LeftBrace))
                {
                    token = parser.Peek();
                    valueExpr = parser.ParseExpression(Precedence);
                }

                if (parser.Match(TokenType.FatArrow))
                {
                    token = parser.Take(TokenType.FatArrow);
                    right = parser.ParseExpression();
                }
                else if (parser.Match(TokenType.LeftBrace))
                {
                    right = parser.ParseBlock(true);
                    token = right.StartToken;
                }

                return new PatternMatchExpression.Pattern(token, typeName, valueExpr, right);
            }
        }
    }
}
