using System.Diagnostics;
using Compiler.Ast.Visitors;
using Compiler.Lexer;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class ExportExpression : Expression
    {
        public FunctionExpression FunctionExpression { get; }

        public ExportExpression(Token token, FunctionExpression functionExpression)
            : base(token)
        {
            FunctionExpression = functionExpression;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
