using Compiler.Lexer;
using Compiler.Parser.Expressions;

namespace Compiler.Parser.Parselets
{
    internal interface IInfixExpressionParselet
    {
        int Precedence { get; }

        Expression Parse(Parser parser, Expression left, Token token);
    }

    internal interface IPrefixExpressionParselet
    {
        Expression Parse(Parser parser, Token token);
    }
}
