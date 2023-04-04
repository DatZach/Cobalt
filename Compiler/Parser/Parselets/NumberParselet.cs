﻿using Compiler.Lexer;
using Compiler.Parser.Expressions;

namespace Compiler.Parser.Parselets
{
    internal sealed class NumberParselet : IPrefixExpressionParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var longValue = long.Parse(token.Value!);
            return new NumberExpression(token, longValue);
        }
    }
}
