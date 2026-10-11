using Compiler.Lexer;
using Compiler.Ast.Visitors;
using System.Diagnostics;

namespace Compiler.Ast.Expressions
{
    internal sealed class NumberLiteralExpression : Expression
    {
        public long LongValue { get; }

        //public eCobType Type { get; }

        //public int BitSize { get; }

        public TypeName TypeName { get; }

        public NumberLiteralExpression(Token token, long longValue, TypeName typeName)
            : base(token)
        {
            LongValue = longValue;
            TypeName = typeName;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
