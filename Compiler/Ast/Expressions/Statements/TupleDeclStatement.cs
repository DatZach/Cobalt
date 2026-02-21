using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class TupleDeclStatement : Expression
    {
        public string Name { get; }

        public IReadOnlyList<GenericDefinition> Generics { get; }

        public IReadOnlyList<FieldDefinition> Fields { get; }

        public IReadOnlyList<FunctionDeclStatement> Functions { get; }

        public IndexerDefinition? Indexer { get; }

        public TupleDeclStatement(
            Token token,
            string name,
            IReadOnlyList<GenericDefinition> generics,
            IReadOnlyList<FieldDefinition> fields,
            IReadOnlyList<FunctionDeclStatement> functions,
            IndexerDefinition? indexer
        )
            : base(token)
        {
            Name = name;
            Generics = generics;
            Fields = fields;
            Functions = functions;
            Indexer = indexer;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }

    internal sealed record GenericDefinition(
        string Name,
        string? ConstraintTypeName
    );

    internal sealed record FieldDefinition(
        string Name,
        string TypeName,
        Expression? GetterExpression,
        Expression? SetterExpression
    );
}
