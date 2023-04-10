using System.Collections;
using System.Runtime.CompilerServices;

namespace Compiler.Lexer
{
    internal sealed partial class Tokenizer
    {
        private readonly static OperatorDictionary Operators;
        private readonly static Dictionary<string, TokenType> Keywords;

        static Tokenizer()
        {
            Operators = new OperatorDictionary
            {
                [";"] = TokenType.Semicolon,
                [","] = TokenType.Comma,
                [":"] = TokenType.Colon,
                ["="] = TokenType.Assign,
                ["..."] = TokenType.Spread,

                ["("] = TokenType.LeftParen,
                [")"] = TokenType.RightParen,
                ["{"] = TokenType.LeftBrace,
                ["}"] = TokenType.RightBrace,
                
                ["+"] = TokenType.Add,
                ["-"] = TokenType.Subtract,
                ["*"] = TokenType.Multiply,
                ["/"] = TokenType.Divide,
                ["%"] = TokenType.Modulo,
                ["<<"] = TokenType.BitLeftShift,
                [">>"] = TokenType.BitRightShift,
                ["&"] = TokenType.BitAnd,
                ["|"] = TokenType.BitOr,
                ["^"] = TokenType.BitXor,

                ["=>"] = TokenType.FatArrow
            };
            
            Keywords = new Dictionary<string, TokenType>
            {
                ["artifact"] = TokenType.Artifact,
                ["import"] = TokenType.Import,
                ["export"] = TokenType.Export,
                ["const"] = TokenType.Const,
                ["fn"] = TokenType.Function,
                ["return"] = TokenType.Return,
                ["aot"] = TokenType.AheadOfTime,

                ["machine"] = TokenType.Machine,
                ["stdcall"] = TokenType.StdCall,
                ["ccall"] = TokenType.CCall,
                ["nakedcall"] = TokenType.NakedCall
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsKeyword(string ident)
        {
            return Keywords.ContainsKey(ident);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsHexChar(char ch)
        {
            return char.IsDigit(ch)
                || (ch >= 'a' && ch <= 'f')
                || (ch >= 'A' && ch <= 'F');
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsBinChar(char ch)
        {
            return ch == '0' || ch == '1';
        }

        private sealed class OperatorDictionary : IEnumerable<object>
        {
            private readonly GenericComparer<Tuple<string, TokenType>> comparer;
            private readonly Dictionary<char, List<Tuple<string, TokenType>>> operatorDictionary;

            public TokenType this[string op]
            {
                get => throw new InvalidOperationException();
                set => Add(op, value);
            }

            public OperatorDictionary()
            {
                comparer = new GenericComparer<Tuple<string, TokenType>>((a, b) => b.Item1.Length - a.Item1.Length);
                operatorDictionary = new Dictionary<char, List<Tuple<string, TokenType>>>();
            }

            public void Add(string op, TokenType type)
            {
                if (!operatorDictionary.TryGetValue(op[0], out var list))
                {
                    list = new List<Tuple<string, TokenType>>();
                    operatorDictionary.Add(op[0], list);
                }

                list.Add(Tuple.Create(op, type));
                list.Sort(comparer);
            }

            public IReadOnlyList<Tuple<string, TokenType>>? Lookup(char ch)
            {
                return !operatorDictionary.TryGetValue(ch, out var list) ? null : list;
            }

            public IEnumerator<object> GetEnumerator()
            {
                throw new InvalidOperationException();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        public sealed class GenericComparer<T> : IComparer<T>
        {
            private readonly Func<T, T, int> comparer;

            public GenericComparer(Func<T, T, int> comparer)
            {
                this.comparer = comparer;
            }

            public int Compare(T x, T y)
            {
                return comparer(x, y);
            }
        }
    }
}
