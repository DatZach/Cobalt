using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class IfParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            parser.Take(TokenType.LeftParen);
            
            Expression? initializer;
            if (parser.Match(TokenType.Var))
            {
                initializer = parser.ParseStatement(false);
                parser.Take(TokenType.Semicolon);
            }
            else
                initializer = null;

            var conditional = parser.ParseExpression(isConditional: true);
            
            parser.Take(TokenType.RightParen);

            var then = parser.ParseBlock();

            var @else = parser.MatchAndTakeToken(TokenType.Else) != null ? parser.ParseBlock() : null;

            if (Parser.IsStatementExpression(then))
                parser.Messages.Add(Message.CannotNakedNestStatement, then);

            return new IfStatement(token, initializer, conditional, then, @else);
        }
    }
}
