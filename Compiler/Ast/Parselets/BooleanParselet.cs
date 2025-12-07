using Compiler.Ast.Expressions;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets
{
    internal sealed class BooleanParselet : IPrefixExpressionParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            return new BooleanExpression(token);
        }
    }
}
