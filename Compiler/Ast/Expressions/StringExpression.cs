using Compiler.Lexer;
using Compiler.Ast.Visitors;
using System.Diagnostics;

namespace Compiler.Ast.Expressions
{
    internal sealed class StringExpression : Expression
    {
        public string Value { get; }

        public StringExpression(Token token, string value)
            : base(token)
        {
            Value = value;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
