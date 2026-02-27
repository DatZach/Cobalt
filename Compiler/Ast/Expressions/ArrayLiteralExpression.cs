using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions
{
    internal sealed class ArrayLiteralExpression : Expression
    {
        public IReadOnlyList<Expression> Elements { get; }

        public ArrayLiteralExpression(Token token, IReadOnlyList<Expression> elements)
            : base(token)
        {
            Elements = elements;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
