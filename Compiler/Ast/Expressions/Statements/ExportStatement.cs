using System.Diagnostics;
using Compiler.Ast.Visitors;
using Compiler.Lexer;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class ExportStatement : Expression
    {
        public FunctionDeclStatement FunctionExpression { get; }

        public ExportStatement(Token token, FunctionDeclStatement functionExpression)
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
