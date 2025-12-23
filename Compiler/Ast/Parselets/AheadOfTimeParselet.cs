using Compiler.Ast.Expressions;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets
{
    internal sealed class AheadOfTimeParselet : IPrefixExpressionParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var expression = parser.ParseExpression();
            return new AheadOfTimeExpression(token, expression);
        }
    }
}
