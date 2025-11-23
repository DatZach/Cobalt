using Compiler.Lexer;
using Compiler.Ast.Visitors;
using Compiler.CodeGeneration;
using System.Diagnostics;

namespace Compiler.Ast.Expressions
{
    internal sealed class FunctionExpression : Expression
    {
        public string Name { get; }

        public bool IsAnonymous { get; }
        
        public CobType ReturnType { get; }

        public CallingConvention CallingConvention { get; }

        public IReadOnlyList<Function.Parameter> Parameters { get; }

        public Expression? Body { get; }

        public FunctionExpression(
            Token token,
            string? name,
            IReadOnlyList<Function.Parameter> parameters,
            Expression? body,
            CobType returnType,
            CallingConvention callingConvention
        )
            : base(token)
        {
            Name = name ?? $"fn_{Path.GetFileNameWithoutExtension(Token.Filename)}_{Token.Line + 1}_{Token.Column + 1}";
            IsAnonymous = name == null;
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
    }
}
