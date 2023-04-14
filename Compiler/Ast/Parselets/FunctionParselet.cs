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

            parser.Take(TokenType.LeftParen);
            if (!parser.Match(TokenType.RightParen))
            {
                var hasSpread = false;
                var lParameters = new List<Function.Parameter>(4);
                while (!parser.Match(TokenType.RightParen))
                {
                    var name = parser.Take(TokenType.Identifier);
                    parser.Take(TokenType.Colon);
                    var isSpread = parser.MatchAndTakeToken(TokenType.Spread) != null;
                    var type = parser.Take(TokenType.Identifier);
                    parser.MatchAndTakeToken(TokenType.Comma);

                    if (isSpread && hasSpread)
                        throw new Exception("Function cannot define multiple spread parameters");

                    hasSpread = hasSpread || isSpread;

                    lParameters.Add(new Function.Parameter(
                        name.Value,
                        CobType.FromString(type.Value),
                        isSpread
                    ));
                }
                
                parameters = lParameters;
            }
            else
                parameters = Array.Empty<Function.Parameter>();

            parser.Take(TokenType.RightParen);

            Expression? body;
            if (parser.Match(TokenType.LeftBrace))
                body = parser.ParseBlock(true);
            else
                body = null;

            string returnType;
            if (parser.Match(TokenType.Identifier))
                returnType = parser.Take(TokenType.Identifier).Value;
            else
                returnType = "None"; // TODO Don't hardcode

            CallingConvention callingConvention;
            if (parser.MatchAndTakeToken(TokenType.CCall) != null)
                callingConvention = CallingConvention.CCall;
            else if (parser.MatchAndTakeToken(TokenType.StdCall) != null)
                callingConvention = CallingConvention.Stdcall;
            //else if (parser.MatchAndTakeToken(TokenType.Naked) != null)
            //    callingConvention = CallingConvention.Naked;
            else
                callingConvention = CallingConvention.CCall; // TODO Don't hardcode

            return new FunctionExpression(
                token,
                parameters,
                body,
                returnType,
                callingConvention
            );
        }
    }
}
