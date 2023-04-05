using Compiler.Lexer;
using Compiler.Ast.Visitors;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class VarExpression : Expression
    {
        public TokenType Type => Token.Type;

        public IReadOnlyList<Declaration> Declarations { get; }

        public VarExpression(Token token, IReadOnlyList<Declaration> declarations)
            : base(token)
        {
            Declarations = declarations;
        }

        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }

        public sealed class Declaration
        {
            public Token Token { get; }

            public Expression? Initializer { get; }

            public string Name => Token.Value!;

            public Declaration(Token token, Expression? initializer)
            {
                Token = token;
                Initializer = initializer;
            }
        }
    }
}
