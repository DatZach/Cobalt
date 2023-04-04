using Compiler.Lexer;
using Compiler.Parser.Expressions;

namespace Compiler.Parser.Parselets.Statements
{
    internal interface IPrefixStatementParselet
    {
        Expression Parse(Parser parser, Token token);
    }
}
