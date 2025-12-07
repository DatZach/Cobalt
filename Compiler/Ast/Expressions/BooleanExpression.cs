using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions
{
    internal sealed class BooleanExpression : Expression
    {
        public bool Value => Token.Type == TokenType.True;

        public BooleanExpression(Token token)
            : base(token)
        {

        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
