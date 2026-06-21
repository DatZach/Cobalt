using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class StructDeclStatement : Expression
    {
        public string Name { get; }

        public IReadOnlyList<GenericDefinition> Generics { get; }

        public IReadOnlyList<TypeName> TraitTypeNames { get; }

        public IReadOnlyList<FieldDefinition> Fields { get; }

        public IReadOnlyList<FunctionDeclStatement> Functions { get; }

        public IReadOnlyList<FactoryDeclStatement> Factories { get; }

        public IndexerDefinition? Indexer { get; }

        public StructDeclStatement(
            Token token,
            string name,
            IReadOnlyList<GenericDefinition> generics,
            IReadOnlyList<TypeName> traitTypeNames,
            IReadOnlyList<FieldDefinition> fields,
            IReadOnlyList<FunctionDeclStatement> functions,
            IReadOnlyList<FactoryDeclStatement> factories,
            IndexerDefinition? indexer
        )
            : base(token)
        {
            Name = name;
            Generics = generics;
            TraitTypeNames = traitTypeNames;
            Fields = fields;
            Functions = functions;
            Factories = factories;
            Indexer = indexer;
        }

        public StructDeclStatement(
            StructDeclStatement other,
            string name
        )
            : this(other.Token, name, other.Generics, other.TraitTypeNames, other.Fields, other.Functions,
                   other.Factories, other.Indexer)
        {

        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }

    internal sealed record IndexerDefinition(
        TypeName KeyTypeName,
        TypeName ReturnTypeName,
        Expression? GetterExpression,
        Expression? SetterExpression
    );
}
