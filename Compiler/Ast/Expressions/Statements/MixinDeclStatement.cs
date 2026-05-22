using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class MixinDeclStatement : Expression
    {
        public string TargetTypeName { get; }

        public FunctionDeclStatement? Function { get; }

        public FieldDefinition? Field { get; }

        public MixinDeclStatement(
            Token token,
            string targetTypeName,
            FunctionDeclStatement? function,
            FieldDefinition? field
        )
            : base(token)
        {
            TargetTypeName = targetTypeName;
            Function = function;
            Field = field;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
