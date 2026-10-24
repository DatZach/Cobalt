using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions
{
    internal sealed class ArrayLiteralExpression : Expression
    {
        public TypeName? ElementTypeName { get; }

        public Expression? CountExpression { get; }

        public IReadOnlyList<Expression> Elements { get; }

        public ArrayLiteralExpression(
            Token token,
            TypeName? elementTypeName,
            Expression? countExpression,
            IReadOnlyList<Expression> elements
        )
            : base(token)
        {
            ElementTypeName = elementTypeName;
            CountExpression = countExpression;
            Elements = elements;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
