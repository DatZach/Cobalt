﻿using System.Runtime.CompilerServices;
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

        private int index;
        private int currentLine;
        private int currentLineStartIndex;

        private readonly StringBuilder stringBuilder;

        private Tokenizer(string filename, string source, MessageCollection messages)
        {
            this.filename = filename ?? throw new ArgumentNullException(nameof(filename));
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.length = source.Length;
            this.messages = messages;

            stringBuilder = new StringBuilder();
        }

        private IReadOnlyList<Token> Tokenize()
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
                var chNext = PeekChar(1);

                // String
                if (ch == '"')
                {
                    var hasInvalidEscape = false;
                    stringBuilder.Clear();

                    var column = startIndex - currentLineStartIndex;

                    TakeChar();
                    while (index < length)
                    {
                        ch = TakeChar();
                        if (ch == '^')
                        {
                            ch = TakeChar() switch
                            {
                                '\'' => '\'',
                                '"' => '\"',
                                '0' => '\0',
                                'a' => '\a',
                                'b' => '\b',
                                'n' => '\n',
                                'r' => '\r',
                                't' => '\t',
                                '^' => '^', // TODO `
                                'x' => (char)Convert.ToByte("" + TakeChar() + TakeChar(), 16),
                                'u' => (char)Convert.ToUInt16("" + TakeChar() + TakeChar() + TakeChar() + TakeChar(), 16),
                                _ => char.MaxValue
                            };

                            hasInvalidEscape = hasInvalidEscape || ch == char.MaxValue;
                        }
                        else if (ch == '\"')
                            break;

                        stringBuilder.Append(ch);
                    }

                    var strToken = new Token
                    {
                        Type = TokenType.String,
                        Value = stringBuilder.ToString(),
                        Filename = filename,
                        Line = startLine,
                        Column = column
                    };

                    if (index >= length)
                        messages.Add(Message.StringUnterminated, strToken);
                    if (hasInvalidEscape)
                        messages.Add(Message.StringInvalidEscape, strToken);

                    result.Add(strToken);
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
                        Filename = filename,
                        Line = currentLine,
                        Column = startIndex - currentLineStartIndex
                    });
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

                    result.Add(new Token
                    {
                        Type = TokenType.Number,
                        Value = ident,
                        Filename = filename,
                        Line = currentLine,
                        Column = startIndex - currentLineStartIndex
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
                            Filename = filename,
                            Line = currentLine,
                            Column = startIndex - currentLineStartIndex
                        });
                        continue;
                    }
                }
                
                ident = TakeWhile(x => !char.IsWhiteSpace(x));

                // ERROR CONDITION - UNEXPECTED TOKEN
                var errToken = new Token
                {
                    Type = TokenType.Error,
                    Value = ident,
                    Filename = filename,
                    Line = currentLine,
                    Column = startIndex - currentLineStartIndex
                };

                messages.Add(Message.UnexpectedToken0, errToken);
                //result.Add(errToken);
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

        public static IReadOnlyList<Token> Tokenize(string filename, string source, MessageCollection messages)
        {
            var tokenizer = new Tokenizer(filename, source, messages);
            return tokenizer.Tokenize();
        }
    }
}
