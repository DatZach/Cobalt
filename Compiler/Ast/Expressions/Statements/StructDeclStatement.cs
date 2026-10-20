using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;
using Compiler.CodeGeneration.Artifacts;

namespace Compiler.Ast.Expressions.Statements
{
    // TODO Rename to RecordDeclStatement
    internal sealed class StructDeclStatement : Expression
    {
        public eRecordType Type { get; }

        public string Name { get; }

        // TODO TypeDeclStatement?
        public IReadOnlyList<GenericDefinition> Generics { get; } // type `TKey [= <constraint>]

        public IReadOnlyList<TypeName> Mixins { get; } // mixin Ident

        public IReadOnlyList<TypeDeclStatement> Types { get; }

        public IReadOnlyList<VariableDeclStatement> Constants { get; }

        public IReadOnlyList<FieldDefinition> Fields { get; }

        public IReadOnlyList<FunctionDeclStatement> Functions { get; }

        // TODO Rename Constructors
        public IReadOnlyList<FactoryDeclStatement> Factories { get; }

        public IReadOnlyList<FunctionDeclStatement> Destructors { get; }

        public IndexerDefinition? Indexer { get; }

        public StructDeclStatement(
            Token token,
            eRecordType type,
            string name,
            IReadOnlyList<GenericDefinition> generics,
            IReadOnlyList<TypeName> mixins,
            IReadOnlyList<TypeDeclStatement> types,
            IReadOnlyList<VariableDeclStatement> constants,
            IReadOnlyList<FieldDefinition> fields,
            IReadOnlyList<FunctionDeclStatement> functions,
            IReadOnlyList<FactoryDeclStatement> factories,
            IReadOnlyList<FunctionDeclStatement> destructors,
            IndexerDefinition? indexer
        )
            : base(token)
        {
            Type = type;
            Name = name;
            Generics = generics;
            Mixins = mixins;
            Types = types;
            Constants = constants;
            Fields = fields;
            Functions = functions;
            Factories = factories;
            Destructors = destructors;
            Indexer = indexer;
        }

        public StructDeclStatement(
            StructDeclStatement other,
            string name
        )
            : this(other.Token, other.Type, name, other.Generics, other.Mixins, other.Types, other.Constants,
                   other.Fields, other.Functions, other.Factories, other.Destructors, other.Indexer)
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
