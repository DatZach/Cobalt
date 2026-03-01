using Compiler.Lexer;
using Compiler.Ast.Expressions;

namespace Compiler.Ast.Parselets
{
    internal sealed class StringLiteralParselet : IPrefixExpressionParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            if (!CharacterLiteralParselet.TryParseEscapedString(token.Value, out var result))
                parser.Messages.Add(Message.StringIllegalEscape, token);

            return new StringLiteralExpression(token, result);
        }
    }
}
