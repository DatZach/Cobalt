using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions.Statements
{
    // type SubType = SuperType [: <refinement-expression>];
    internal sealed class TypeDeclStatement : Expression
    {
        public string SubTypeName { get; }

        public TypeName SuperTypeName { get; }

        public Expression? RefinementExpression { get; }

        public TypeDeclStatement(Token token, string subTypeName, TypeName superTypeName, Expression? refinementExpression)
            : base(token)
        {
            SubTypeName = subTypeName;
            SuperTypeName = superTypeName;
            RefinementExpression = refinementExpression;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }

    internal sealed class EnumRefinementDeclStatement : Expression
    {
        public IReadOnlyList<EnumMember> Members { get; }

        public EnumRefinementDeclStatement(Token token, IReadOnlyList<EnumMember> members)
            : base(token)
        {
            Members = members;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }

        public sealed record EnumMember(string Name, Expression? Value);
    }
}
