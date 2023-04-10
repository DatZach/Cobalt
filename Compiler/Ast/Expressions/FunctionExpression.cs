using Compiler.Lexer;
using Compiler.Ast.Visitors;
using Compiler.CodeGeneration;
using System.Diagnostics;

namespace Compiler.Ast.Expressions
{
    internal sealed class FunctionExpression : Expression
    {
        public string Name => $"fn_{Token.Line}_{Token.Column}";
        
        public string ReturnType { get; }

        public CallingConvention CallingConvention { get; }

        public IReadOnlyList<Parameter> Parameters { get; }

        public Expression? Body { get; }

        public FunctionExpression(
            Token token,
            IReadOnlyList<Parameter> parameters,
            Expression? body,
            string returnType,
            CallingConvention callingConvention
        )
            : base(token)
        {
            Parameters = parameters;
            Body = body;
            ReturnType = returnType;
            CallingConvention = callingConvention;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }

        public sealed record Parameter
        {
            public string Name { get; init; }

            public string Type { get; init; }

            public bool IsSpread { get; init; }
        }
    }
}
