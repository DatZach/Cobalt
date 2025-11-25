using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;
using Compiler.CodeGeneration;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class TypeExpression : Expression
    {
        public string Name { get; }

        public CobType Type { get; }

        public TypeExpression(Token token, string name, CobType type)
            : base(token)
        {
            Name = name;
            Type = type;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
