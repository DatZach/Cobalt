using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class FactoryDeclParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            IReadOnlyList<FunctionDeclStatement.Parameter> parameters;

            var name = parser.Take(TokenType.Identifier);
            
            parser.Take(TokenType.LeftParen);
            if (!parser.Match(TokenType.RightParen))
            {
                var hasSpread = false;
                var lParameters = new List<FunctionDeclStatement.Parameter>(4);
                while (!parser.Match(TokenType.RightParen))
                {
                    var isSpread = parser.MatchAndTakeToken(TokenType.Spread) != null;

                    var paramName = parser.Take(TokenType.Identifier);
                    parser.Take(TokenType.Colon);
                    var paramType = parser.ParseTypeName();
                    parser.MatchAndTakeToken(TokenType.Comma);

                    if (isSpread && hasSpread)
                        parser.Messages.Add(Message.ExcessiveSpreadParameters, paramName);

                    hasSpread = hasSpread || isSpread;

                    lParameters.Add(new FunctionDeclStatement.Parameter(paramName.Value, paramType, isSpread));
                }

                parameters = lParameters;
            }
            else
                parameters = Array.Empty<FunctionDeclStatement.Parameter>();

            parser.Take(TokenType.RightParen);

            Token? fatArrowToken;
            Expression? body;
            if (parser.Match(TokenType.LeftBrace))
                body = parser.ParseBlock(true);
            else if ((fatArrowToken = parser.MatchAndTakeToken(TokenType.FatArrow)) != null)
            {
                body = new ReturnStatement(
                    fatArrowToken,
                    parser.ParseStatement(false)
                );
            }
            else
            {
                parser.Messages.Add(Message.MissingFunctionBody, token);
                body = new EmptyExpression(token);
            }

            return new FactoryDeclStatement(token, name.Value, parameters, body);
        }
    }
}
