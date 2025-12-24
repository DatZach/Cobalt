using Compiler.Ast.Visitors;
using Compiler.CodeGeneration;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class TupleDeclStatement : Expression
    {
        public string Name { get; }

        public IReadOnlyList<FieldDefinition> Fields { get; }

        public IReadOnlyList<FunctionDeclStatement> Functions { get; }

        public TupleDeclStatement(
            Token token,
            string name,
            IReadOnlyList<FieldDefinition> fields,
            IReadOnlyList<FunctionDeclStatement> functions
        )
            : base(token)
        {
            Name = name;
            Fields = fields;
            Functions = functions;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }

    internal sealed record FieldDefinition(
        string Name,
        string TypeName,
        Expression? GetterExpression,
        Expression? SetterExpression
    );
}
