using Compiler.Lexer;
using Compiler.Ast.Visitors;
using System.Diagnostics;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class ImportStatement : Expression
    {
        public string SourceFile { get; }

        public string? SymbolName { get; }

        public TypeName? SymbolTypeName { get; }

        public ImportStatement(
            Token token,
            string sourceFile,
            string? symbolName,
            TypeName? symbolTypeName
        )
            : base(token)
        {
            SourceFile = sourceFile;
            SymbolName = symbolName;
            SymbolTypeName = symbolTypeName;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
