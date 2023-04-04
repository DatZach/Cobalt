using Compiler.Lexer;
using Compiler.Parser.Expressions;

namespace Compiler.Parser.Parselets
{
    internal sealed class CallParselet : IInfixExpressionParselet
    {
        public int Precedence => PrecedenceTable.FunctionCall;

        public Expression Parse(Parser parser, Expression left, Token token)
        {
            IReadOnlyList<Expression> arguments;
            
            if (!parser.Match(TokenType.RightParen))
            {
                var lArguments = new List<Expression>(4);

                do
                {
                    lArguments.Add(parser.ParseExpression(isConditional: true));
                } while (parser.MatchAndTakeToken(TokenType.Comma) != null);

                arguments = lArguments;
            }
            else
                arguments = Array.Empty<Expression>();

            parser.Take(TokenType.RightParen);

            return new CallExpression(token, left, arguments);
        }
    }
}
