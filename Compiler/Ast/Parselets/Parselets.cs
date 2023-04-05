using Compiler.Lexer;
using Compiler.Ast.Expressions;

namespace Compiler.Ast.Parselets
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
