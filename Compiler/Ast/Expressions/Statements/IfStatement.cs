using System.Diagnostics;
using Compiler.Ast.Visitors;
using Compiler.Lexer;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class IfStatement : Expression
    {
        public Expression Conditional { get; }

        public Expression Then { get; }

        public Expression? Else { get; }

        public IfStatement(Token token, Expression conditional, Expression then, Expression? @else)
            : base(token)
        {
            Conditional = conditional;
            Then = then;
            Else = @else;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
