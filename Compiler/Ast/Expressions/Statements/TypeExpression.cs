using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class TypeExpression : Expression
    {
        public string Name { get; }

        public string TypeName { get; }

        public TypeExpression(Token token, string name, string typeName)
            : base(token)
        {
            Name = name;
            TypeName = typeName;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
