using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class WhileStatement : Expression
    {
        public Expression? Initializer { get; }

        public Expression? Conditional { get; }

        public Token? Tag { get; }

        public Expression Body { get; }

        public WhileStatement(
            Token token,
            Expression? initializer,
            Expression? conditional,
            Token? tag,
            Expression body
        )
            : base(token)
        {
            Initializer = initializer;
            Conditional = conditional;
            Tag = tag;
            Body = body;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
