using Compiler.Ast.Expressions;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets
{
    internal sealed class PostfixOperatorParselet : IInfixExpressionParselet
    {
        public int Precedence { get; }

        public PostfixOperatorParselet(int precedence)
        {
            Precedence = precedence;
        }

        public Expression Parse(Parser parser, Expression left, Token token)
        {
            if (token.Type is TokenType.Range or TokenType.RangeInclusive
                           or TokenType.RangeLength or TokenType.RangeTerminal)
            {
                var empty = new EmptyExpression(token);
                return new BinaryOperatorExpression(token, left, empty);
            }

            return new PostfixOperatorExpression(token, left);
        }
    }
}
