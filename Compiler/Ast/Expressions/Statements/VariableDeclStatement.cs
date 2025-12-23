using Compiler.Lexer;
using Compiler.Ast.Visitors;
using System.Diagnostics;
using Compiler.CodeGeneration;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class VariableDeclStatement : Expression
    {
        public TokenType Type => Token.Type;

        public IReadOnlyList<Declaration> Declarations { get; }

        public override Token EndToken => Declarations.LastOrDefault()?.Initializer?.EndToken ?? Token;

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

        public sealed class Declaration
        {
            public Token Token { get; }

            public string? TypeName { get; }

            public CobType? Type => TypeName != null ? CobType.FromString(TypeName) : null;

            public Expression? Initializer { get; }

            public string Name => Token.Value!;

            public Declaration(Token token, string? typeName, Expression? initializer)
            {
                Token = token;
                TypeName = typeName;
                Initializer = initializer;
            }
        }
    }
}
