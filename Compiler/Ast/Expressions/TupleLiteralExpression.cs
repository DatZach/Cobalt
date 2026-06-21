using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions
{
    internal sealed class TupleLiteralExpression : Expression
    {
        public TypeName? ExplicitTypeName { get; }

        public IReadOnlyList<Expression> Expressions { get; }

        public override Token EndToken { get; }

        public TupleLiteralExpression(Token token, Token endToken, IReadOnlyList<Expression> expressions)
            : base(token)
        {
            EndToken = endToken;
            Expressions = expressions;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
