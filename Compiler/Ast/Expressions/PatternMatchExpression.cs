using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;
using Compiler.CodeGeneration.Artifacts;

namespace Compiler.Ast.Expressions
{
    internal sealed class PatternMatchExpression : Expression
    {
        public Expression Left { get; }

        public Pattern? RightSingle { get; }

        public IReadOnlyList<Pattern>? RightMulti { get; }

        public PatternMatchExpression(
            Token token,
            Expression left,
            Pattern? rightSingle,
            IReadOnlyList<Pattern>? rightMulti
        )
            : base(token)
        {
            Left = left;
            RightSingle = rightSingle;
            RightMulti = rightMulti;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }

        public sealed record Pattern(Token? Token, string? TypeName, Expression? ValueExpression, Expression? Right)
        {
            public CobType? Type => TypeName == null ? null : CobType.FromString(TypeName);
        }
    }
}
