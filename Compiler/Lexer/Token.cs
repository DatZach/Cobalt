using System.ComponentModel;

namespace Compiler.Lexer
{
    internal sealed record Token(
        TokenType Type,
        string Value,
        string Filename,
        int Line,
        int Column
    )
    {
        public readonly static Token EndOfStream = new(TokenType.EndOfStream, "", "", 0, 0);

        public override string ToString()
        {
            switch (Type)
            {
                case TokenType.Identifier:
                case TokenType.Number:
                case TokenType.String:
                    var value = Value;
                    if (value.Length > 16)
                        value = value[..13] + "...";

                    return $"{Type}('{value}')";

                default:
                    return Type.GetDescription();
            }
        }
    }

    public enum TokenType
    {
        [Description("identifier")] Identifier,
        [Description("number")] Number,
        [Description("string")] String,
        [Description("character")] Character,
        [Description("true")] True,
        [Description("false")] False,
        [Description("nil")] Nil,
        
        [Description("artifact")] Artifact,
        [Description("import")] Import,
        [Description("export")] Export,

        [Description("module")] Module,
        [Description("using")] Using,
        [Description("struct")] Struct,
        [Description("trait")] Trait,
        [Description("type")] Type,
        [Description("tuple")] Tuple,
        [Description("func")] Function,
        [Description("mixin")] Mixin,
        [Description("factory")] Factory,
        [Description("packed")] Packed,
        [Description("enum")] Enum,
        [Description("bitflags")] BitFlags,
        [Description("error")] Error,

        [Description("const")] Const,
        [Description("var")] Var,

        [Description("machine")] Machine,
        [Description("shell")] Shell,
        [Description("defer")] Defer,
        [Description("if")] If,
        [Description("else")] Else,
        [Description("for")] For,
        [Description("while")] While,
        [Description("in")] In,
        [Description("given")] Given,
        [Description("return")] Return,
        [Description("break")] Break,
        [Description("continue")] Continue,
        [Description("lens")] Lens,
        [Description("aot")] AheadOfTime,
        [Description("jit")] JustInTime,
        [Description("unchecked")] Unchecked,

        [Description("ccall")] CCall,
        [Description("stdcall")] StdCall,
        [Description("nakedcall")] NakedCall,

        [Description("';'")] Semicolon,
        [Description("'('")] LeftParen,
        [Description("')'")] RightParen,
        [Description("'{'")] LeftBrace,
        [Description("'}'")] RightBrace,
        [Description("'['")] LeftSquare,
        [Description("']'")] RightSquare,
        [Description("','")] Comma,
        [Description("'.'")] Dot,
        [Description("':'")] Colon,
        [Description("'`'")] Generic,
        [Description("'...'")] Spread,
        [Description("'..'")] Range,
        [Description("'..='")] RangeInclusive,
        [Description("'..+'")] RangeLength,
        [Description("'..^'")] RangeTerminal,
        [Description("'[?'")] ArrayWhere,
        [Description("'[='")] ArraySelect,
        [Description("'=>'")] FatArrow,

        [Description("'+'")] Add,
        [Description("'-'")] Subtract,
        [Description("'*'")] Multiply,
        [Description("'**'")] Exponent,
        [Description("'/'")] Divide,
        [Description("'/^'")] DivideCeil,
        [Description("'/~'")] DivideFloor,
        [Description("'%'")] Remainder,
        [Description("'%%'")] Modulo,
        [Description("'<<'")] BitLeftShift,
        [Description("'>>'")] BitRightShift,
        [Description("'<<<'")] BitLeftRotate,
        [Description("'>>>'")] BitRightRotate,
        [Description("'<<|'")] BitPack,
        [Description("'&'")] BitAnd,
        [Description("'|'")] BitOr,
        [Description("'^'")] BitXor,
        [Description("'!'")] Not,
        [Description("'~'")] BitNot,
        [Description("'?'")] Question,

        [Description("'&&'")] ConditionalAnd,
        [Description("'||'")] ConditionalOr,
        [Description("'=='")] Equals,
        [Description("'!='")] NotEquals,
        [Description("'<='")] LessThanOrEquals,
        [Description("'>='")] MoreThanOrEquals,
        [Description("'<'")] LessThan,
        [Description("'>'")] MoreThan,

        [Description("'='")] Assign,
        [Description("'+='")] AddAssign,
        [Description("'-='")] SubtractAssign,
        [Description("'*='")] MultiplyAssign,
        [Description("'/='")] DivideAssign,
        [Description("'/^='")] DivideCeilAssign,
        [Description("'/~='")] DivideFloorAssign,
        [Description("'%='")] RemainderAssign,
        [Description("'%%='")] ModuloAssign,
        [Description("'<<='")] BitLeftShiftAssign,
        [Description("'>>='")] BitRightShiftAssign,
        [Description("'<<<='")] BitLeftRotateAssign,
        [Description("'>>>='")] BitRightRotateAssign,
        [Description("'&='")] BitAndAssign,
        [Description("'|='")] BitOrAssign,
        [Description("'^='")] BitXorAssign,

        [Description("::")] Is,
        [Description("!:")] IsNot,

        [Description("'?.'")] NilDot,
        [Description("'??'")] NilCoalesce,
        [Description("'!.'")] ErrorDot,
        [Description("'!!'")] ErrorCoalesce,
        [Description("'?!'")] NilErrorCoalesce,

        [Description("'@'")] At,

        [Description("<illegal token>")] IllegalToken,
        [Description("<end-of-stream>")] EndOfStream
    }

    internal static class TokenUtility
    {
        public static string GetDescription(this Enum value)
        {
            var type = value.GetType();
            var name = Enum.GetName(type, value);
            if (name == null)
                return "(unknown)";

            var field = type.GetField(name);
            if (field == null)
                return "(unknown)";
            
            if (Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) is DescriptionAttribute attr)
                return attr.Description;

            return "(unknown)";
        }
    }
}
