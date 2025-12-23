using Compiler.Lexer;
using Compiler.Ast.Visitors;
using System.Diagnostics;
using Compiler.CodeGeneration;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class ImportStatement : Expression
    {
        public string SourceFile { get; }

        public string? SymbolName { get; }

        public CobType? SymbolType { get; }

        public FunctionDeclStatement? SymbolTypeSignature { get; }

        public ImportStatement(
            Token token,
            string sourceFile,
            string? symbolName,
            CobType? symbolType,
            FunctionDeclStatement? functionSignature
        )
            : base(token)
        {
            SourceFile = sourceFile;
            SymbolName = symbolName;
            SymbolType = symbolType;
            SymbolTypeSignature = functionSignature;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
