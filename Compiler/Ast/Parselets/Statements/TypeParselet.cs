using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class TypeParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var name = parser.Take(TokenType.Identifier);
            var typeName = parser.ParseTypeName();

            return new TypeExpression(token, name.Value, typeName);
        }
    }
}
