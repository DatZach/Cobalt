using System.Diagnostics;
using Compiler.Ast.Visitors;
using Compiler.Lexer;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class ArtifactStatement : Expression
    {
        public string Container { get; } // pe, joe

        public string Architecture { get; } // x86, x86_64, cobalt

        public string? Filename { get; }

        public IReadOnlyList<BinaryOperatorExpression>? ContainerParameters { get; }

        public ArtifactStatement(
            Token token,
            string container,
            string architecture,
            string? filename,
            IReadOnlyList<BinaryOperatorExpression>? containerParameters
        )
            : base(token)
        {
            Container = container;
            Architecture = architecture;
            Filename = filename;
            ContainerParameters = containerParameters;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
