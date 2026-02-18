using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class FactoryDeclStatement : Expression
    {
        public string Name { get; }

        public IReadOnlyList<FunctionDeclStatement.Parameter> Parameters { get; }

        public Expression Body { get; }

        public FactoryDeclStatement(
            Token token,
            string name,
            IReadOnlyList<FunctionDeclStatement.Parameter> parameters,
            Expression body
        )
            : base(token)
        {
            Name = name;
            Parameters = parameters;
            Body = body;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
