using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class DestructStatement : Expression
    {
        public string? Name { get; }

        public IReadOnlyList<FunctionDeclStatement.Parameter> Parameters { get; }

        public TypeName? ReturnTypeName { get; }

        public Expression? Body { get; }

        public DestructStatement(
            Token token,
            string? name,
            IReadOnlyList<FunctionDeclStatement.Parameter> parameters,
            TypeName? returnTypeName,
            Expression? body
        )
            : base(token)
        {
            Name = name;
            Parameters = parameters;
            ReturnTypeName = returnTypeName;
            Body = body;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
