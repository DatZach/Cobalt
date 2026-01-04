using Compiler.Lexer;
using Compiler.Ast.Visitors;
using System.Diagnostics;
using Compiler.CodeGeneration.Artifacts;

namespace Compiler.Ast.Expressions
{
    internal sealed class NumberLiteralExpression : Expression
    {
        public long LongValue { get; }

        public eCobType Type { get; }

        public int BitSize { get; }

        public NumberLiteralExpression(Token token, long longValue, eCobType type, int bitSize)
            : base(token)
        {
            LongValue = longValue;
            Type = type;
            BitSize = bitSize;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
