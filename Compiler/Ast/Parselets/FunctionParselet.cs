using Compiler.Lexer;
using Compiler.Ast.Expressions;
using Compiler.CodeGeneration;

namespace Compiler.Ast.Parselets
{
    internal sealed class FunctionParselet : IPrefixExpressionParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            IReadOnlyList<FunctionExpression.Parameter> parameters;

            parser.Take(TokenType.LeftParen);
            if (!parser.Match(TokenType.RightParen))
            {
                var lParameters = new List<FunctionExpression.Parameter>(4);
                while (!parser.Match(TokenType.RightParen))
                {
                    var name = parser.Take(TokenType.Identifier);
                    
                    var type = parser.Take(TokenType.Identifier);
                    lParameters.Add(new FunctionExpression.Parameter
                    {
                        Name = name.Value,
                        Type = type.Value
                    });
                }
                
                parameters = lParameters;
            }
            else
                parameters = Array.Empty<FunctionExpression.Parameter>();

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
