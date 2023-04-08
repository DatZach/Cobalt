using Compiler.Ast.Visitors;
using Compiler.Lexer;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class ArtifactExpression : Expression
    {
        public string TargetPlaform { get; }

        public string? Filename { get; }

        public ArtifactExpression(Token token, string targetPlatform, string? filename)
            : base(token)
        {
            TargetPlaform = targetPlatform;
            Filename = filename;
        }

        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
