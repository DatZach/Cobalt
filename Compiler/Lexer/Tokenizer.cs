using System.Runtime.CompilerServices;
using System.Text;
using Compiler.Ast;

namespace Compiler.Lexer
{
    internal sealed partial class Tokenizer
    {
        private readonly string filename;
        private readonly string source;
        private readonly int length;
        private readonly MessageCollection messages;
        private readonly List<Token> tokens;

        private int index;
        private int currentLine;
        private int currentLineStartIndex;
        private int startLine;
        private int startColumn;

        private readonly StringBuilder stringBuilder;

        private Tokenizer(string source, string filename, MessageCollection messages)
        {
            this.filename = filename ?? throw new ArgumentNullException(nameof(filename));
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.length = source.Length;
            this.messages = messages;

            tokens = new List<Token>(4);
            stringBuilder = new StringBuilder();
        }

        private IReadOnlyList<Token> Tokenize()
        {
            while (index < length)
            {
                if (IgnoreWhitespace())
                    break;

                if (IgnoreComments())
                    continue;

                string ident;
                startLine = currentLine;
                startColumn = index - currentLineStartIndex;
                var ch = PeekChar();
                var chNext = PeekChar(1);

                // String
                if (ch == '"')
                {
                    var hasInvalidEscape = false;
                    stringBuilder.Clear();

                    TakeChar();
                    while (index < length)
                    {
                        ch = TakeChar();
                        if (ch == '\"')
                            break;

                        stringBuilder.Append(ch);
                    }

                    var strToken = YieldToken(TokenType.String, stringBuilder.ToString());

                    if (index >= length)
                        messages.Add(Message.StringUnterminated, strToken);
                    if (hasInvalidEscape)
                        messages.Add(Message.StringIllegalEscape, strToken);

                    continue;
                }

                // Keyword/Identifier
                if (char.IsLetter(ch) || ch == '_')
                {
                    ident = TakeWhile(c => char.IsLetterOrDigit(c) || c == '_');
                    YieldToken(
                        Keywords.TryGetValue(ident, out var keywordType) ? keywordType : TokenType.Identifier,
                        ident
                    );
                    continue;
                }

                // Number
                if (char.IsDigit(ch)
                ||  (ch == '-' && char.IsDigit(chNext))
                ||  (ch == '+' && char.IsDigit(chNext)))
                {
                    bool hasHexSpecified = false;
                    bool hasBinSpecified = false;
                    bool hasDecimal = false;
                    bool hasSign = false;
                    bool hasType = false;

                    if (ch == '0' && chNext == 'x')
                        hasHexSpecified = true;
                    else if (ch == '0' && chNext == 'b')
                        hasBinSpecified = true;

                    var startIndex = index;
                    ident = TakeWhile(c =>
                    {
                        if (c == '_')
                            return true;

                        if (hasHexSpecified)
                        {
                            if (c == 'x' && index == startIndex + 1)
                                return true;
                            return IsHexChar(c);
                        }

                        if (hasBinSpecified)
                        {
                            if (c == 'b' && index == startIndex + 1)
                                return true;
                            return IsBinChar(c);
                        }

                        if (!hasSign && (c == '-' || c == '+'))
                        {
                            hasSign = true;
                            return true;
                        }

                        if (!hasDecimal && c == '.')
                        {
                            hasDecimal = true;
                            return true;
                        }

                        if (!hasType && (c == 'u' || c == 's' || c == 'f'))
                        {
                            hasType = true;
                            return true;
                        }

                        return char.IsDigit(c);
                    });

                    YieldToken(TokenType.Number, ident);
                    continue;
                }

                // Operators
                var opList = Operators.Lookup(ch);
                if (opList != null)
                {
                    Tuple<string, TokenType>? op = null;
                    for (var i = 0; i < opList.Count; i++)
                    {
                        var o = opList[i];
                        if (TakeIfNext(o.Item1))
                        {
                            op = o;
                            break;
                        }
                    }

                    if (op != null)
                    {
                        YieldToken(op.Item2, op.Item1);
                        continue;
                    }
                }
                
                ident = TakeWhile(x => !char.IsWhiteSpace(x));

                // ERROR CONDITION - UNEXPECTED TOKEN
                var errToken = YieldToken(TokenType.Error, ident);
                messages.Add(Message.UnexpectedToken0, errToken);
            }

            return tokens;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Token YieldToken(TokenType type, string value)
        {
            var token = new Token(
                type,
                value,
                filename,
                startLine,
                startColumn
            );
            tokens.Add(token);
            return token;
        }

        private bool IgnoreWhitespace()
        {
            while(index < length)
            {
                if (!char.IsWhiteSpace(PeekChar()))
                    break;
                
                TakeChar();
            }

            return index >= length;
        }

        private bool IgnoreComments()
        {
            var ch = PeekChar();
            if (ch == '/')
            {
                if (TakeIfNext("//"))
                {
                    while (index < length && PeekChar() != '\n')
                        TakeChar();

                    return true;
                }
                
                if (TakeIfNext("/*"))
                {
                    int stack = 1;
                    while (index < length && stack > 0)
                    {
                        if (TakeIfNext("*/"))
                            --stack;
                        else if (TakeIfNext("/*"))
                            ++stack;
                        else
                            TakeChar();
                    }
                    
                    return true;
                }
            }

            return false;
        }

        // NOTE value cannot contain a linefeed or else things break
        private bool TakeIfNext(string value)
        {
            if (!IsNext(value))
                return false;

            index += value.Length;

            return true;
        }

        private bool TakeIfNext(char value)
        {
            if (PeekChar() != value)
                return false;

            TakeChar();
            return true;
        }

        private bool IsNext(string value)
        {
            if (index + value.Length > length)
                return false;

            var span = source.AsSpan(index, value.Length);
            return span.SequenceEqual(value.AsSpan());
        }

        // TODO ReadOnlySpan?
        private string TakeWhile(Func<char, bool> condition)
        {
            int i = index;
            while(index < length)
            {
                if (!condition(PeekChar()))
                    break;

                TakeChar();
            }

            return source.Substring(i, index - i);
        }

        private char TakeChar()
        {
            char result = PeekChar();
            ++index;

            if (result == '\n')
            {
                ++currentLine;
                currentLineStartIndex = index;
            }

            return result;
        }

        private char PeekChar(int offset)
        {
            offset += index;
            return offset < length ? source[offset] : '\0';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private char PeekChar()
        {
            return index < length ? source[index] : '\0';
        }

        public static IReadOnlyList<Token> Tokenize(string source, string filename, MessageCollection messages)
        {
            var tokenizer = new Tokenizer(source, filename, messages);
            return tokenizer.Tokenize();
        }
    }
}
