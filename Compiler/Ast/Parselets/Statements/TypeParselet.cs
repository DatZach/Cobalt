using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.CodeGeneration;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class TypeParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var name = parser.Take(TokenType.Identifier);
            var type = parser.ParseTypeName();

            if (!CobType.TryAddAlias(name.Value, type))
                parser.Messages.Add(Message.SymbolConflictsWithOther, token, name.Value);

            return new TypeExpression(token, name.Value, type);
        }
    }
}
