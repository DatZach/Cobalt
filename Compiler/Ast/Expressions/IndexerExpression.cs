using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions
{
    internal sealed class IndexerExpression : Expression
    {
        public Expression Left { get; }

        public Expression Index { get; }

        public override Token EndToken { get; }

        public IndexerExpression(Token token, Token endToken, Expression left, Expression index)
            : base(token)
        {
            EndToken = endToken;
            Left = left;
            Index = index;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
