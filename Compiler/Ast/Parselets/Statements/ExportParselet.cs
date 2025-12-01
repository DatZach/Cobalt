using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class ExportParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var functionExpression = parser.ParseStatement() as FunctionExpression;
            if (functionExpression == null)
                parser.Messages.Add(Message.UnexpectedToken2, token, "function", token);

            return new ExportExpression(token, functionExpression);
        }
    }
}
