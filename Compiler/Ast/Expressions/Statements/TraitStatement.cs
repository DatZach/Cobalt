using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class TraitStatement : Expression
    {
        public string Name { get; }

        public IReadOnlyList<FunctionDeclStatement> Functions { get; }

        public TraitStatement(Token token, string name, IReadOnlyList<FunctionDeclStatement> functions)
            : base(token)
        {
            Name = name;
            Functions = functions;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
