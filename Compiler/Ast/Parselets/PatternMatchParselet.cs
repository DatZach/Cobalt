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
            // x !:: int
            // x :: int
            // x :: { int y => y, default => 0 }
            // x :: { 1 => 1234, default => 4321 }
            // x :: { error => 0, default => 1 }

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

            // 0
            // 1, 2, 3
            // .Monday
            // string x
            // nil
            // int 0, 1, 2
            PatternMatchExpression.Pattern ParsePattern()
            {
                Expression? valueExpr = null;
                Expression? right = null;

                var token = parser.Peek();
                var typeName = parser.ParseTypeName();

                if (parser.Match(TokenType.Identifier) || parser.Match(TokenType.Dot)
                ||  parser.Match(TokenType.Number) || parser.Match(TokenType.String)
                ||  parser.Match(TokenType.Character)
                ||  parser.Match(TokenType.True) || parser.Match(TokenType.False))
                    valueExpr = parser.ParseExpression(Precedence);

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
