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

            if (parser.Match(TokenType.Assign))
            {
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
            else if (parser.Match(TokenType.Mixin))
            {
                parser.Take(TokenType.Mixin);
                var traitTypeName = parser.ParseTypeName();
                var record = parser.Match(TokenType.LeftBrace)
                           ? (RecordStatement)RecordParselet.Instance.Parse(parser, parser.Take())
                           : null;
                return new MixinDeclStatement(token, name.Value, null, null, traitTypeName, record);
            }
            else
            {
                parser.Messages.Add(Message.UnexpectedToken2, token, "'=' or 'mixin'", parser.Take());
                return new EmptyExpression(token);
            }
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
