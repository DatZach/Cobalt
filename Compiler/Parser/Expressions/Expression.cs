using System.Diagnostics;
using Compiler.Lexer;
using Compiler.Parser.Visitors;

namespace Compiler.Parser.Expressions
{
    internal abstract class Expression
    {
        public Token Token { get; }

        protected Expression(Token token)
        {
            Token = token;
        }

        [DebuggerStepThrough]
        public abstract T Accept<T>(IExpressionVisitor<T> visitor);
    }
}
