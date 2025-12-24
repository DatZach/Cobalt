using Compiler.Lexer;
using Compiler.Ast.Visitors;
using Compiler.CodeGeneration;
using System.Diagnostics;

namespace Compiler.Ast.Expressions
{
    internal sealed class FunctionDeclStatement : Expression
    {
        public string Name { get; }

        public bool IsAnonymous { get; }

        public string? ReturnTypeName { get; }

        public CobType ReturnType => CobType.FromString(ReturnTypeName);

        public CallingConvention CallingConvention { get; }

        public IReadOnlyList<Parameter> Parameters { get; }

        public Expression? Body { get; }

        public FunctionDeclStatement(
            Token token,
            string? name,
            IReadOnlyList<Parameter> parameters,
            Expression? body,
            string? returnTypeName,
            CallingConvention callingConvention
        )
            : base(token)
        {
            Name = name ?? $"fn_{Path.GetFileNameWithoutExtension(Token.Filename)}_{Token.Line + 1}_{Token.Column + 1}";
            IsAnonymous = name == null;
            Parameters = parameters;
            Body = body;
            ReturnTypeName = returnTypeName;
            CallingConvention = callingConvention;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }

        public sealed record Parameter(
            string Name,
            string TypeName,
            bool IsSpread
        );
    }
}
