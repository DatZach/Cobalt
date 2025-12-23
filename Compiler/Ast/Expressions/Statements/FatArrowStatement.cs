using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class FatArrowStatement : Expression
    {
        public Expression Expression { get; }

        public FatArrowStatement(Token token, Expression expression)
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
