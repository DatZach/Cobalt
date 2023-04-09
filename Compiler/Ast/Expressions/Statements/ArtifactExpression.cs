using Compiler.Ast.Visitors;
using Compiler.Lexer;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class ArtifactExpression : Expression
    {
        public string Container { get; } // pe, joe

        public IReadOnlyList<string> ContainerParameters { get; } // +gui, +console

        public string Platform { get; } // x86, x86_64, cobalt

        public string? Filename { get; }

        public ArtifactExpression(
            Token token,
            string container,
            IReadOnlyList<string> containerParameters,
            string platform,
            string? filename
        )
            : base(token)
        {
            Container = container;
            ContainerParameters = containerParameters;
            Platform = platform;
            Filename = filename;
        }

        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
