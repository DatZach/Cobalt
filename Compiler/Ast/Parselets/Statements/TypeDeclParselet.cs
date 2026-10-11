using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class TypeDeclParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var name = parser.Take(TokenType.Identifier);
            parser.Take(TokenType.Assign);
            var typeName = parser.ParseTypeName();
            if (typeName == null)
            {
                parser.Messages.Add(Message.ExpectedTypeName, token);
                typeName = TypeName.Any;
            }

            var refinementExpression = parser.MatchAndTakeToken(TokenType.Colon) != null
                                     ? parser.ParseExpression()
                                     : null;

            return new TypeDeclStatement(token, name.Value, typeName, refinementExpression);
        }
    }
}
