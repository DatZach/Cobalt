using Compiler.Lexer;
using Compiler.Ast.Expressions;
using Compiler.CodeGeneration;

namespace Compiler.Ast.Parselets
{
    internal sealed class FunctionParselet : IPrefixExpressionParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            IReadOnlyList<Function.Parameter> parameters;

            var name = parser.MatchAndTakeToken(TokenType.Identifier)?.Value;

            parser.Take(TokenType.LeftParen);
            if (!parser.Match(TokenType.RightParen))
            {
                var hasSpread = false;
                var lParameters = new List<Function.Parameter>(4);
                while (!parser.Match(TokenType.RightParen))
                {
                    var isSpread = parser.MatchAndTakeToken(TokenType.Spread) != null;

                    var paramName = parser.Take(TokenType.Identifier);
                    parser.Take(TokenType.Colon);

                    string typeName;
                    var type = parser.Take(TokenType.Identifier);
                    var typeLs = parser.MatchAndTakeToken(TokenType.LeftSquare);
                    var typeRs = parser.MatchAndTakeToken(TokenType.RightSquare);
                    if (typeLs != null && typeRs != null)
                        typeName = type.Value + "[]";
                    else
                        typeName = type.Value;

                    parser.MatchAndTakeToken(TokenType.Comma);

                    if (isSpread && hasSpread)
                        parser.Messages.Add(Message.ExcessiveSpreadParameters, paramName);

                    hasSpread = hasSpread || isSpread;

                    lParameters.Add(new Function.Parameter(
                        paramName.Value,
                        CobType.FromString(typeName),
                        isSpread
                    ));
                }

                parameters = lParameters;
            }
            else
                parameters = Array.Empty<Function.Parameter>();

            parser.Take(TokenType.RightParen);

            var sReturnType = parser.MatchAndTakeToken(TokenType.Identifier)?.Value;
            var returnType = CobType.FromString(sReturnType);

            CallingConvention callingConvention;
            if (parser.MatchAndTakeToken(TokenType.CCall) != null)
                callingConvention = CallingConvention.CCall;
            else if (parser.MatchAndTakeToken(TokenType.StdCall) != null)
                callingConvention = CallingConvention.Stdcall;
            //else if (parser.MatchAndTakeToken(TokenType.Naked) != null)
            //    callingConvention = CallingConvention.Naked;
            else
                callingConvention = CallingConvention.CCall; // TODO Don't hardcode

            Expression? body;
            if (parser.Match(TokenType.LeftBrace))
                body = parser.ParseBlock(true);
            else
                body = null;

            return new FunctionExpression(
                token,
                name,
                parameters,
                body,
                returnType,
                callingConvention
            );
        }
    }
}