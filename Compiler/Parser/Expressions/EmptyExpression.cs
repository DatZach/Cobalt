﻿using Compiler.Lexer;
using Compiler.Parser.Visitors;

namespace Compiler.Parser.Expressions
{
    internal sealed class EmptyExpression : Expression
    {
        public EmptyExpression(Token token)
            : base(token)
        {

        }

        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
