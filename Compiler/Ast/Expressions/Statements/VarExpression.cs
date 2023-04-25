using Compiler.Lexer;
using Compiler.Ast.Visitors;
using System.Diagnostics;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class VarExpression : Expression
    {
        public TokenType Type => Token.Type;

        public IReadOnlyList<Declaration> Declarations { get; }

        public override Token EndToken => Declarations.LastOrDefault()?.Initializer?.EndToken ?? Token;

        public VarExpression(Token token, IReadOnlyList<Declaration> declarations)
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
