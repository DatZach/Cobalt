using Compiler.Lexer;
using Compiler.Ast.Visitors;
using System.Diagnostics;
using Compiler.CodeGeneration.Artifacts;

namespace Compiler.Ast.Expressions
{
    internal sealed class FunctionDeclStatement : Expression
    {
        public string Name { get; }

        public bool IsAnonymous { get; }

        public IReadOnlyList<TypeName> Generics { get; }

        public Token? Operator { get; }

        public TypeName? ReturnTypeName { get; }

        public CallingConvention CallingConvention { get; }

        public IReadOnlyList<Parameter> Parameters { get; }

        public Expression? Body { get; }

        public FunctionDeclStatement(
            Token token,
            string? name,
            IReadOnlyList<TypeName> generics,
            Token? @operator,
            IReadOnlyList<Parameter> parameters,
            Expression? body,
            TypeName? returnTypeName,
            CallingConvention callingConvention
        )
            : base(token)
        {
            Name = name ?? $"fn_{Path.GetFileNameWithoutExtension(Token.Filename)}_{Token.Line + 1}_{Token.Column + 1}";
            IsAnonymous = name == null;
            Generics = generics;
            Operator = @operator;
            Parameters = parameters;
            Body = body;
            ReturnTypeName = returnTypeName;
            CallingConvention = callingConvention;
        }

        public FunctionDeclStatement(
            FunctionDeclStatement other,
            string name
        )
            : this(other.Token, name, other.Generics, other.Operator, other.Parameters, other.Body, other.ReturnTypeName,
                   other.CallingConvention)
        {

        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }

        public sealed record Parameter(
            string Name,
            TypeName TypeName,
            bool IsSpread,
            Expression? DefaultValue
        );
    }
}
