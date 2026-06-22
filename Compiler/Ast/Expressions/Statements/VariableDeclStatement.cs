using Compiler.Lexer;
using Compiler.Ast.Visitors;
using System.Diagnostics;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class VariableDeclStatement : Expression
    {
        public TokenType Type => Token.Type;

        public IReadOnlyList<Declaration> Declarations { get; }

        public override Token EndToken => Declarations.LastOrDefault()?.EndToken ?? Token;

        public VariableDeclStatement(Token token, IReadOnlyList<Declaration> declarations)
            : base(token)
        {
            Declarations = declarations;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }

        public sealed class StandardDeclaration : Declaration
        {
            public string Name { get; }

            public TypeName? TypeName { get; }

            public Expression? Initializer { get; }

            public override Token? EndToken => Initializer?.EndToken;

            public StandardDeclaration(string name, TypeName? typeName, Expression? initializer)
            {
                Name = name;
                TypeName = typeName;
                Initializer = initializer;
            }
        }

        public sealed class DestructureDeclaration : Declaration
        {
            public IReadOnlyList<Field> Fields { get; }

            public Expression? Initializer { get; }

            public override Token? EndToken => Initializer?.EndToken;

            public DestructureDeclaration(IReadOnlyList<Field> fields, Expression? initializer)
            {
                Fields = fields;
                Initializer = initializer;
            }

            public sealed class Field
            {
                public string Name { get; }

                public TypeName? TypeName { get; }

                public Field(string name, TypeName? typeName)
                {
                    Name = name;
                    TypeName = typeName;
                }
            }
        }

        public abstract class Declaration
        {
            public abstract Token? EndToken { get; }
        }
    }
}
