using Compiler.Ast.Expressions;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets
{
    internal sealed class LensParselet : IPrefixExpressionParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var elementTypeName = parser.ParseTypeName();
            var expression = parser.ParseExpression();

            return new LensExpression(token, elementTypeName, expression);
        }
    }
}
