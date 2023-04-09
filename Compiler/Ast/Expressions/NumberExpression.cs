using Compiler.Lexer;
using Compiler.Ast.Visitors;
using System.Diagnostics;

namespace Compiler.Ast.Expressions
{
    internal sealed class NumberExpression : Expression
    {
        public long LongValue { get; }

        public NumberExpression(Token token, long longValue)
            : base(token)
        {
            LongValue = longValue;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
