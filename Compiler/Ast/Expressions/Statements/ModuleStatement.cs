using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class ModuleStatement : Expression
    {
        public string? Name { get; }

        public Expression? Block { get; }

        public ModuleStatement(Token token, string? name, Expression? block)
            : base(token)
        {
            Name = name;
            Block = block;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
