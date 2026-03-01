using Compiler.Lexer;
using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.CodeGeneration.Artifacts;

namespace Compiler.Ast.Parselets
{
    internal sealed class FunctionDeclParselet : IPrefixExpressionParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            IReadOnlyList<FunctionDeclStatement.Parameter> parameters;

            var name = parser.MatchAndTakeToken(TokenType.Identifier)?.Value;

            parser.Take(TokenType.LeftParen);
            if (!parser.Match(TokenType.RightParen))
            {
                var hasSpread = false;
                var lParameters = new List<FunctionDeclStatement.Parameter>(4);
                while (!parser.Match(TokenType.RightParen))
                {
                    var isSpread = parser.MatchAndTakeToken(TokenType.Spread) != null;

                    var paramName = parser.Take(TokenType.Identifier);
                    var paramType = parser.MatchAndTakeToken(TokenType.Colon) != null
                                  ? parser.ParseTypeName()
                                  : nameof(CobType.Any);

                    var paramDefault = parser.MatchAndTakeToken(TokenType.Assign) != null
                                     ? parser.ParseExpression()
                                     : null;

                    parser.MatchAndTakeToken(TokenType.Comma);

                    if (isSpread && hasSpread)
                        parser.Messages.Add(Message.ExcessiveSpreadParameters, paramName);

                    hasSpread = hasSpread || isSpread;

                    lParameters.Add(new FunctionDeclStatement.Parameter(paramName.Value, paramType, isSpread, paramDefault));
                }

                parameters = lParameters;
            }
            else
                parameters = Array.Empty<FunctionDeclStatement.Parameter>();

            parser.Take(TokenType.RightParen);

            var returnTypeName = parser.Match(TokenType.Identifier) ? parser.ParseTypeName() : null;

            CallingConvention callingConvention;
            if (parser.MatchAndTakeToken(TokenType.CCall) != null)
                callingConvention = CallingConvention.CCall;
            else if (parser.MatchAndTakeToken(TokenType.StdCall) != null)
                callingConvention = CallingConvention.StdCall;
            else if (parser.MatchAndTakeToken(TokenType.NakedCall) != null)
                callingConvention = CallingConvention.NakedCall;
            else
                callingConvention = CallingConvention.Default;

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
                body = null;

            return new FunctionDeclStatement(
                token,
                name,
                parameters,
                body,
                returnTypeName,
                callingConvention
            );
        }
    }
}