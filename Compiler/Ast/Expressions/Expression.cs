using System.Diagnostics;
using Compiler.Lexer;
using Compiler.Ast.Visitors;

namespace Compiler.Ast.Expressions
{
    internal abstract class Expression
    {
        public Token Token { get; }

        public virtual Token StartToken => Token;

        public virtual Token EndToken => Token;

        protected Expression(Token token)
        {
            Token = token;
        }

        [DebuggerStepThrough]
        public abstract T Accept<T>(IExpressionVisitor<T> visitor);
    }
}
