using Compiler.Ast.Visitors;
using Compiler.CodeGeneration;
using Compiler.Lexer;
using System.Diagnostics;
using Compiler.CodeGeneration.Artifacts;

namespace Compiler.Ast.Expressions
{
    internal sealed class LensExpression : Expression
    {
        public CobType ElementType { get; }

        public Expression Expression { get; }

        public LensExpression(Token token, CobType elementType, Expression expression)
            : base(token)
        {
            ElementType = elementType;
            Expression = expression;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
