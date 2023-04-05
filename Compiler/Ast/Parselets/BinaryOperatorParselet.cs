using Compiler.Lexer;
using Compiler.Ast.Expressions;

namespace Compiler.Ast.Parselets
{
    internal sealed class BinaryOperatorParselet : IInfixExpressionParselet
    {
        public int Precedence { get; }

        public BinaryOperatorParselet(int precedence)
        {
            Precedence = precedence;
        }

        public Expression Parse(Parser parser, Expression left, Token token)
        {
            var right = parser.ParseExpression(Precedence, false);
            return new BinaryOperatorExpression(token, left, right);
        }
    }
}
