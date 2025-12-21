using Compiler.Ast.Expressions;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets
{
    internal sealed class PrefixOperatorParselet : IPrefixExpressionParselet
    {
        private readonly int precedence;

        public PrefixOperatorParselet(int precedence)
        {
            this.precedence = precedence;
        }

        public Expression Parse(Parser parser, Token token)
        {
            var right = parser.ParseExpression(precedence);

            if (token.Type is TokenType.Range or TokenType.RangeInclusive
                           or TokenType.RangeLength or TokenType.RangeTerminal)
            {
                var empty = new EmptyExpression(token);
                return new BinaryOperatorExpression(token, empty, right);
            }

            return new PrefixOperatorExpression(token, right);
        }
    }
}
