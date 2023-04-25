using System.Text;
using Compiler.Lexer;
using Compiler.Ast.Expressions;

namespace Compiler.Ast.Parselets
{
    internal sealed class StringParselet : IPrefixExpressionParselet
    {
        private const char EscapeCharacter = '`';

        public Expression Parse(Parser parser, Token token)
        {
            var value = token.Value;
            if (!value.Contains(EscapeCharacter))
                return new StringExpression(token, token.Value);

            var length = value.Length;
            var sb = new StringBuilder(length);
            for (int i = 0; i < length; ++i)
            {
                var ch = value[i];
                if (ch != '`')
                    continue;

                var isIllegalEscape = false;
                if (i + 1 < length)
                {
                    ch = value[++i];
                    if (ch == '\'')
                        ch = '\'';
                    else if (ch == '"')
                        ch = '\"';
                    else if (ch == '0')
                        ch = '\0';
                    else if (ch == 'a')
                        ch = '\a';
                    else if (ch == 'b')
                        ch = '\b';
                    else if (ch == 'n')
                        ch = '\n';
                    else if (ch == 'r')
                        ch = '\r';
                    else if (ch == 't')
                        ch = '\t';
                    else if (ch == EscapeCharacter)
                        ch = EscapeCharacter;
                    else if (ch == 'x' && i + 2 < length)
                    {
                        if (!TryParseByte(value.Substring(i + 1, 2), out ch))
                            isIllegalEscape = true;
                    }
                    else if (ch == 'u' && i + 4 < length)
                    {
                        if (!TryParseUnicode(value.Substring(i + 1, 4), out ch))
                            isIllegalEscape = true;
                    }
                    else
                        isIllegalEscape = true;
                }
                else
                    isIllegalEscape = true;

                if (isIllegalEscape)
                    parser.Messages.Add(Message.StringIllegalEscape, token);
                
                sb.Append(ch);
            }

            return new StringExpression(token, sb.ToString());
        }

        private static bool TryParseByte(string value, out char result)
        {
            try
            {
                result = (char)Convert.ToByte(value, 16);
                return true;
            }
            catch
            {
                result = '\0';
                return false;
            }
        }

        private static bool TryParseUnicode(string value, out char result)
        {
            try
            {
                result = (char)Convert.ToUInt16(value, 16);
                return true;
            }
            catch
            {
                result = '\0';
                return false;
            }
        }
    }
}
