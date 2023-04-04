using Compiler.Lexer;
using Compiler.Parser.Visitors;

namespace Compiler.Parser.Expressions
{
    internal sealed class FunctionExpression : Expression
    {
        public string Name => $"fn_{Token.Line}_{Token.Column}";

        public IReadOnlyList<Parameter> Parameters { get; }

        public Expression? Body { get; }

        public FunctionExpression(Token token, IReadOnlyList<Parameter> parameters, Expression? body)
            : base(token)
        {
            Parameters = parameters;
            Body = body;
        }

        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }

        public sealed record Parameter
        {

        }
    }
}
