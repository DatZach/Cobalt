using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class ExportParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var functionExpression = parser.ParseStatement() as FunctionDeclStatement;
            if (functionExpression == null)
                parser.Messages.Add(Message.UnexpectedToken2, token, "function", token);

            return new ExportStatement(token, functionExpression);
        }
    }
}
