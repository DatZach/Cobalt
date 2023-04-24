using System.ComponentModel;

namespace Compiler.Lexer
{
    internal sealed record Token
    {
        public TokenType Type { get; init; }

        public string Value { get; init; }

        public string Filename { get; init; }

        public int Line { get; init; }

        public int Column { get; init; }

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
        [Description("delimiter")] Delimiter,
        
        [Description("artifact")] Artifact,
        [Description("import")] Import,
        [Description("export")] Export,
        [Description("const")] Const,
        [Description("fn")] Function,
        [Description("return")] Return,
        [Description("aot")] AheadOfTime,

        [Description("abi")] CCall,
        [Description("stdcall")] StdCall, // TODO ??
        [Description("naked")] NakedCall,
        [Description("machine")] Machine,

        [Description("';'")] Semicolon,
        [Description("'('")] LeftParen,
        [Description("')'")] RightParen,
        [Description("'{'")] LeftBrace,
        [Description("'}'")] RightBrace,
        [Description("','")] Comma,
        [Description("':'")] Colon,
        [Description("'...'")] Spread,

        [Description("'=>'")] FatArrow,
        [Description("'+'")] Add,
        [Description("'-'")] Subtract,
        [Description("'*'")] Multiply,
        [Description("'/'")] Divide,
        [Description("'%'")] Modulo,
        [Description("'<<'")] BitLeftShift,
        [Description("'>>'")] BitRightShift,
        [Description("'&'")] BitAnd,
        [Description("'|'")] BitOr,
        [Description("'^'")] BitXor,

        [Description("'='")] Assign,

        [Description("<error token>")] Error,
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
