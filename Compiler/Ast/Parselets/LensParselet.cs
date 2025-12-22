using Compiler.Ast.Expressions;
using Compiler.CodeGeneration;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets
{
    internal sealed class LensParselet : IPrefixExpressionParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var elementType = CobType.FromString(parser.ParseTypeName());
            var expression = parser.ParseExpression();

            return new LensExpression(token, elementType, expression);
        }
    }
}
