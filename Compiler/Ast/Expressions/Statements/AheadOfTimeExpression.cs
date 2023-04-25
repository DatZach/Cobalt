﻿using System.Diagnostics;
using Compiler.Ast.Visitors;
using Compiler.Lexer;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class AheadOfTimeExpression : Expression
    {
        public Expression Expression { get; }

        public override Token EndToken => Expression.EndToken;

        public AheadOfTimeExpression(Token token, Expression expression)
            : base(token)
        {
            Expression = expression;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
