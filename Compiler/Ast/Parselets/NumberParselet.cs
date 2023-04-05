using Compiler.Lexer;
using Compiler.Ast.Expressions;

namespace Compiler.Ast.Parselets
{
    internal sealed class NumberParselet : IPrefixExpressionParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var longValue = long.Parse(token.Value!);
            return new NumberExpression(token, longValue);
        }
    }
}
