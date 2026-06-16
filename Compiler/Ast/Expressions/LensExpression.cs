using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions
{
    internal sealed class LensExpression : Expression
    {
        public string ElementTypeName { get; }

        public Expression Expression { get; }

        public LensExpression(Token token, string elementTypeName, Expression expression)
            : base(token)
        {
            ElementTypeName = elementTypeName;
            Expression = expression;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
