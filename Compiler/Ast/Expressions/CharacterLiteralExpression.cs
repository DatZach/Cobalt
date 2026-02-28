using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions
{
    internal sealed class CharacterLiteralExpression : Expression
    {
        public char Value { get; }

        public CharacterLiteralExpression(Token token, char value)
            : base(token)
        {
            Value = value;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
