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

        public TypeName? TraitTypeName { get;  }

        public RecordStatement? Record { get; }

        public MixinDeclStatement(
            Token token,
            string targetTypeName,
            FunctionDeclStatement? function,
            FieldDefinition? field,
            TypeName? traitTypeName,
            RecordStatement? record
        )
            : base(token)
        {
            TargetTypeName = targetTypeName;
            Function = function;
            Field = field;
            TraitTypeName = traitTypeName;
            Record = record;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
