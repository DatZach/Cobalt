using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class MachineStatement : Expression
    {
        public string Target { get; }

        public string Source { get; }

        public MachineStatement(Token token, string target, string source)
            : base(token)
        {
            Target = target;
            Source = source;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
