using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class TraitParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var name = parser.Take(TokenType.Identifier);

            var functions = new List<FunctionDeclStatement>();

            parser.Take(TokenType.LeftBrace);
            while (parser.MatchAndTakeToken(TokenType.RightBrace) == null)
            {
                var expr = parser.ParseStatement();
                if (expr is not FunctionDeclStatement function)
                {
                    parser.Messages.Add(Message.UnexpectedToken2, expr, "function", expr);
                    continue;
                }

                functions.Add(function);
            }

            return new TraitStatement(token, name.Value, functions);
        }
    }
}
