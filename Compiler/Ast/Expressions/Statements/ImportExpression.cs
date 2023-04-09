using Compiler.Lexer;
using Compiler.Ast.Visitors;
using System.Diagnostics;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class ImportExpression : Expression
    {
        public string Library { get; }

        public string? SymbolName { get; }

        public FunctionExpression? FunctionSignture { get; }


        public ImportExpression(Token token, string library, string? symbolName, FunctionExpression? functionSignature)
            : base(token)
        {
            Library = library;
            SymbolName = symbolName;
            FunctionSignture = functionSignature;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
