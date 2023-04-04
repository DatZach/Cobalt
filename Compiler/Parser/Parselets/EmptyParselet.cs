using Compiler.Lexer;
using Compiler.Parser.Expressions;

namespace Compiler.Parser.Parselets
{
    internal sealed class EmptyParselet : IPrefixExpressionParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            return new EmptyExpression(token);
        }
    }
}
