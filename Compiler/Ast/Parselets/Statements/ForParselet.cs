using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class ForParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            parser.Take(TokenType.LeftParen);
            parser.Take(TokenType.Var);
            var valueIdentifier = parser.Take(TokenType.Identifier).Value;
            var keyIdentifier = parser.MatchAndTakeToken(TokenType.Comma) != null
                              ? parser.Take(TokenType.Identifier).Value
                              : null;
            parser.Take(TokenType.In);
            var enumerable = parser.ParseExpression();
            var generator = parser.MatchAndTakeToken(TokenType.Given) != null
                          ? parser.ParseExpression()
                          : null;
            var conditional = parser.MatchAndTakeToken(TokenType.Semicolon) != null
                            ? parser.ParseExpression(isConditional: true)
                            : null;
            var isContinue = parser.MatchAndTakeToken(TokenType.Continue) != null
                          && parser.MatchAndTakeToken(TokenType.Break) == null;
            parser.Take(TokenType.RightParen);

            var label = parser.MatchAndTakeToken(TokenType.At) != null ? parser.Take(TokenType.Identifier) : null;

            var body = parser.ParseBlock();

            if (Parser.IsStatementExpression(body))
                parser.Messages.Add(Message.CannotNakedNestStatement, body);

            return new ForStatement(
                token,
                valueIdentifier,
                keyIdentifier,
                enumerable,
                generator,
                conditional,
                isContinue,
                label,
                body
            );
        }
    }
}
