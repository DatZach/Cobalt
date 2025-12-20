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
                ["("] = TokenType.LeftParen,
                [")"] = TokenType.RightParen,
                ["{"] = TokenType.LeftBrace,
                ["}"] = TokenType.RightBrace,
                ["["] = TokenType.LeftSquare,
                ["]"] = TokenType.RightSquare,
                [","] = TokenType.Comma,
                ["."] = TokenType.Dot,
                [":"] = TokenType.Colon,
                ["..."] = TokenType.Spread,
                [".."] = TokenType.Range,
                ["..="] = TokenType.RangeInclusive,
                ["..+"] = TokenType.RangeLength,
                ["..^"] = TokenType.RangeTerminal,
                ["[?"] = TokenType.ArrayWhere,
                ["[="] = TokenType.ArraySelect,
                ["=>"] = TokenType.FatArrow,
                
                ["+"] = TokenType.Add,
                ["-"] = TokenType.Subtract,
                ["*"] = TokenType.Multiply,
                ["**"] = TokenType.Exponent,
                ["/"] = TokenType.Divide,
                ["/^"] = TokenType.DivideCeil,
                ["/_"] = TokenType.DivideFloor,
                ["%"] = TokenType.Remainder,
                ["%%"] = TokenType.Modulo,
                ["<<"] = TokenType.BitLeftShift,
                [">>"] = TokenType.BitRightShift,
                ["<<<"] = TokenType.BitLeftRotate,
                [">>>"] = TokenType.BitRightRotate,
                ["<<|"] = TokenType.BitPack,
                ["&"] = TokenType.BitAnd,
                ["|"] = TokenType.BitOr,
                ["^"] = TokenType.BitXor,
                ["!"] = TokenType.Not,

                ["&&"] = TokenType.ConditionalAnd,
                ["||"] = TokenType.ConditionalOr,
                ["=="] = TokenType.Equals,
                ["!="] = TokenType.NotEquals,
                ["<="] = TokenType.LessThanOrEquals,
                [">="] = TokenType.MoreThanOrEquals,
                ["<"] = TokenType.LessThan,
                [">"] = TokenType.MoreThan,
                
                ["="] = TokenType.Assign,
                ["+="] = TokenType.AddAssign,
                ["-="] = TokenType.SubtractAssign,
                ["*="] = TokenType.MultiplyAssign,
                ["/="] = TokenType.DivideAssign,
                ["/^="] = TokenType.DivideCeilAssign,
                ["/_="] = TokenType.DivideFloorAssign,
                ["%="] = TokenType.RemainderAssign,
                ["%%="] = TokenType.ModuloAssign,
                ["<<="] = TokenType.BitLeftShiftAssign,
                [">>="] = TokenType.BitRightShiftAssign,
                ["<<<="] = TokenType.BitLeftRotateAssign,
                [">>>="] = TokenType.BitRightRotateAssign,
                ["&="] = TokenType.BitAndAssign,
                ["|="] = TokenType.BitOrAssign,
                ["^="] = TokenType.BitXorAssign,

                ["?."] = TokenType.NilDot,
                ["??"] = TokenType.NilCoalesce,
                ["!."] = TokenType.ErrorDot,
                ["!!"] = TokenType.ErrorCoalesce
            };
            
            Keywords = new Dictionary<string, TokenType>
            {
                ["artifact"] = TokenType.Artifact,
                ["import"] = TokenType.Import,
                ["export"] = TokenType.Export,

                ["module"] = TokenType.Module,
                ["using"] = TokenType.Using,
                ["struct"] = TokenType.Struct,
                ["trait"] = TokenType.Trait,
                ["type"] = TokenType.Type,
                ["tuple"] = TokenType.Tuple,
                ["func"] = TokenType.Function,
                ["mixin"] = TokenType.Mixin,
                ["factory"] = TokenType.Factory,
                ["packed"] = TokenType.Packed,
                ["enum"] = TokenType.Enum,
                ["bitflags"] = TokenType.BitFlags,
                ["error"] = TokenType.Error,

                ["true"] = TokenType.True,
                ["false"] = TokenType.False,

                ["const"] = TokenType.Const,
                ["var"] = TokenType.Var,

                ["machine"] = TokenType.Machine,
                ["shell"] = TokenType.Shell,
                ["defer"] = TokenType.Defer,
                ["if"] = TokenType.If,
                ["else"] = TokenType.Else,
                ["is"] = TokenType.Is,
                ["for"] = TokenType.For,
                ["in"] = TokenType.In,
                ["return"] = TokenType.Return,
                ["break"] = TokenType.Break,
                ["continue"] = TokenType.Continue,
                ["lens"] = TokenType.Lens,
                ["aot"] = TokenType.AheadOfTime,
                ["jit"] = TokenType.JustInTime,
                ["unchecked"] = TokenType.Unchecked,

                ["ccall"] = TokenType.CCall,
                ["stdcall"] = TokenType.StdCall,
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
