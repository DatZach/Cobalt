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

            Expression? refinementExpression;
            if (parser.MatchAndTakeToken(TokenType.Colon) != null)
            {
                if (parser.Match(TokenType.LeftBrace))
                    refinementExpression = ParseEnumExpression(parser);
                else
                    refinementExpression = parser.ParseExpression();
            }
            else
                refinementExpression = null;

            return new TypeDeclStatement(token, name.Value, typeName, refinementExpression);
        }

        private static Expression ParseEnumExpression(Parser parser)
        {
            var members = new List<EnumRefinementDeclStatement.EnumMember>();

            var token = parser.Take(TokenType.LeftBrace);
            while (parser.MatchAndTakeToken(TokenType.RightBrace) == null)
            {
                var name = parser.Take(TokenType.Identifier);
                var value = parser.MatchAndTakeToken(TokenType.Assign) != null
                          ? parser.ParseExpression()
                          : null;
                members.Add(new EnumRefinementDeclStatement.EnumMember(name, value));

                parser.MatchAndTakeToken(TokenType.Comma);
            }

            return new EnumRefinementDeclStatement(token, members);
        }
    }
}
