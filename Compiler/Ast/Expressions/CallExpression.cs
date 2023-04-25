using Compiler.Lexer;
using Compiler.Ast.Visitors;
using System.Diagnostics;

namespace Compiler.Ast.Expressions
{
    internal sealed class CallExpression : Expression
    {
        public Expression FunctionExpression { get; }

        public IReadOnlyList<Expression> Arguments { get; }

        public override Token StartToken => FunctionExpression.StartToken;

        public override Token EndToken { get; }

        public CallExpression(Token token, Token endToken, Expression functionExpression,
                              IReadOnlyList<Expression> arguments)
            : base(token)
        {
            EndToken = endToken;
            FunctionExpression = functionExpression;
            Arguments = arguments;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
