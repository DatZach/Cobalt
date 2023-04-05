using Compiler.Lexer;
using Compiler.Ast.Expressions;

namespace Compiler.Ast.Parselets.Statements
{
    internal interface IPrefixStatementParselet
    {
        Expression Parse(Parser parser, Token token);
    }
}
