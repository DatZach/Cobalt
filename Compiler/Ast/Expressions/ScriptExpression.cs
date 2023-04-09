using Compiler.Lexer;
using Compiler.Ast.Visitors;
using System.Diagnostics;

namespace Compiler.Ast.Expressions
{
    internal sealed class ScriptExpression : Expression
    {
        public IReadOnlyList<Expression> Expressions { get; }

        public ScriptExpression(Token token, IReadOnlyList<Expression> expressions)
            : base(token)
        {
            Expressions = expressions;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
