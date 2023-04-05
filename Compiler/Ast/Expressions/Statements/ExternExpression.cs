using Compiler.Lexer;
using Compiler.Ast.Visitors;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class ExternExpression : Expression
    {
        public string Library { get; }

        public string SymbolName { get; }

        public FunctionExpression? FunctionSignture { get; }


        public ExternExpression(Token token, string library, string symbolName, FunctionExpression? functionSignature)
            : base(token)
        {
            Library = library;
            SymbolName = symbolName;
            FunctionSignture = functionSignature;
        }

        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
