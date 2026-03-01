using Compiler.Ast.Expressions;
using Compiler.Lexer;
using System.Text;

namespace Compiler.Ast.Parselets
{
    internal sealed class CharacterLiteralParselet : IPrefixExpressionParselet
    {
        private const char EscapeCharacter = '^';

        public Expression Parse(Parser parser, Token token)
        {
            if (!TryParseEscapedString(token.Value, out var result))
                parser.Messages.Add(Message.StringIllegalEscape, token);
            else if (result.Length != 1)
                parser.Messages.Add(Message.CharacterIllegalLength, token);

            return new CharacterLiteralExpression(token, result.FirstOrDefault());
        }

        public static bool TryParseEscapedString(string value, out string result)
        {
            if (!value.Contains(EscapeCharacter))
            {
                result = value;
                return true;
            }

            var hasIllegalEscape = false;
            var length = value.Length;
            var sb = new StringBuilder(length);
            for (int i = 0; i < length; ++i)
            {
                var ch = value[i];
                if (ch == EscapeCharacter)
                {
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
                                hasIllegalEscape = true;
                        }
                        else if (ch == 'u' && i + 4 < length)
                        {
                            if (!TryParseUnicode(value.Substring(i + 1, 4), out ch))
                                hasIllegalEscape = true;
                        }
                        else
                            hasIllegalEscape = true;
                    }
                    else
                        hasIllegalEscape = true;
                }

                sb.Append(ch);
            }

            result = sb.ToString();
            return !hasIllegalEscape;
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
