namespace Compiler.Lexer
{
    internal sealed record Token
    {
        public TokenType Type { get; init; }

        public string Value { get; init; }

        public int Line { get; init; }

        public int Column { get; init; }

        public override string ToString()
        {
            return $"{Type} ('{Value}')";
        }
    }

    public enum TokenType
    {
        Identifier,
        Number,
        String,
        Delimiter,
        
        Artifact,
        Import,
        Export,
        Const,
        Function,
        Return,
        AheadOfTime,

        CCall,
        StdCall,
        NakedCall,
        Machine,

        Semicolon,
        LeftParen,
        RightParen,
        LeftBrace,
        RightBrace,
        Comma,
        Colon,
        Spread,

        FatArrow,
        Add,
        Subtract,
        Multiply,
        Divide,
        Modulo,
        BitLeftShift,
        BitRightShift,
        BitAnd,
        BitOr,
        BitXor,

        Assign,

        Error,
        EndOfStream
    }
}
