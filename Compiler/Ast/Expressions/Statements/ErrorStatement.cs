using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class ErrorStatement : Expression
    {
        public IReadOnlyList<string> Members { get; }

        public ErrorStatement(Token token, IReadOnlyList<string> members)
            : base(token)
        {
            Members = members;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
