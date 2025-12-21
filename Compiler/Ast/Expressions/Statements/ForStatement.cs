using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class ForStatement : Expression
    {
        public Expression? Conditional { get; }

        public Expression? Expression { get; }

        public Token? Label { get; }

        public Expression Body { get; }

        public ForStatement(
            Token token,
            Expression? conditional,
            Expression? expression,
            Token? label,
            Expression body
        )
            : base(token)
        {
            Conditional = conditional;
            Expression = expression;
            Label = label;
            Body = body;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
