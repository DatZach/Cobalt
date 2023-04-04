using System.Runtime.CompilerServices;

namespace Compiler.Lexer
{
    internal sealed partial class Tokenizer
    {
        private readonly string source;
        private readonly int length;

        private int index;
        private int currentLine;
        private int currentLineStartIndex;

        public Tokenizer(string source)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.length = source.Length;
        }

        public IReadOnlyList<Token> Tokenize()
        {
            var result = new List<Token>(4);
            
            while (index < length)
            {
                if (IgnoreWhitespace())
                    break;

                if (IgnoreComments())
                    continue;

                string ident;
                var startLine = currentLine;
                var startIndex = index;
                var ch = PeekChar();

                // String
                if (ch == '"')
                {
                    bool isEscape = false;
                    int i = index;

                    TakeChar();
                    while (index < length)
                    {
                        ch = TakeChar();
                        if (ch == '"' && !isEscape)
                            break;
                        
                        isEscape = !isEscape && ch == '\\';
                    }

                    if (index >= length)
                        throw new Exception("Unterminated string");

                    result.Add(new Token
                    {
                        Type = TokenType.String,
                        Value = source.Substring(i + 1, index - i - 2),
                        Line = startLine,
                        Column = startIndex - currentLineStartIndex + 1
                    });
                    continue;
                }

                // Keyword/Identifier
                if (char.IsLetter(ch) || ch == '_')
                {
                    ident = TakeWhile(c => char.IsLetterOrDigit(c) || c == '_');
                    result.Add(new Token
                    {
                        Type = Keywords.TryGetValue(ident, out var keywordType) ? keywordType : TokenType.Identifier,
                        Value = ident,
                        Line = currentLine,
                        Column = startIndex - currentLineStartIndex + 1
                    });
                    continue;
                }

                // Number
                if (char.IsDigit(ch))
                {
                    ident = TakeWhile(c =>
                    {
                        if (c == '_')
                            return true;
                        
                        return char.IsDigit(c);
                    });

                    result.Add(new Token
                    {
                        Type = TokenType.Number,
                        Value = ident,
                        Line = currentLine,
                        Column = startIndex - currentLineStartIndex + 1,
                    });
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
                        result.Add(new Token
                        {
                            Type = op.Item2,
                            Value = op.Item1,
                            Line = currentLine,
                            Column = startIndex - currentLineStartIndex + 1,
                        });
                        continue;
                    }
                }
                
                ident = TakeWhile(x => !char.IsWhiteSpace(x));

                // ERROR CONDITION - UNEXPECTED TOKEN
                result.Add(new Token
                {
                    Type = TokenType.Error,
                    Value = ident,
                    Line = currentLine,
                    Column = startIndex - currentLineStartIndex + 1
                });
            }

            return result;
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
    }
}
