﻿using Compiler.Lexer;
using Compiler.Parser.Visitors;

namespace Compiler.Parser.Expressions
{
    internal sealed class CallExpression : Expression
    {
        public Expression FunctionExpression { get; }

        public IReadOnlyList<Expression> Arguments { get; }

        public CallExpression(Token token, Expression functionExpression, IReadOnlyList<Expression> arguments)
            : base(token)
        {
            FunctionExpression = functionExpression;
            Arguments = arguments;
        }

        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
