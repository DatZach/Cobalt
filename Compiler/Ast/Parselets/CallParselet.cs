﻿using Compiler.Lexer;
using Compiler.Ast.Expressions;

namespace Compiler.Ast.Parselets
{
    internal sealed class CallParselet : IInfixExpressionParselet
    {
        public int Precedence => PrecedenceTable.FunctionCall;

        public Expression Parse(Parser parser, Expression left, Token token)
        {
            IReadOnlyList<Expression> arguments;
            
            if (!parser.Match(TokenType.RightParen))
            {
                var lArguments = new List<Expression>(4);

                do
                {
                    lArguments.Add(parser.ParseExpression(isConditional: true));
                } while (parser.MatchAndTakeToken(TokenType.Comma) != null);

                arguments = lArguments;
            }
            else
                arguments = Array.Empty<Expression>();

            var endToken = parser.Take(TokenType.RightParen);

            return new CallExpression(token, endToken, left, arguments);
        }
    }
}
