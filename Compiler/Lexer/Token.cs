﻿namespace Compiler.Lexer
{
    internal sealed record Token
    {
        public TokenType Type { get; init; }

        public string? Value { get; init; }

        public int Line { get; init; }

        public int Column { get; init; }

        public override string ToString()
        {
            return $"{Type} ('{Value ?? ""}')";
        }
    }

    public enum TokenType
    {
        Identifier,
        Number,
        Delimiter,
        
        Const,

        Semicolon,
        LeftParen,
        RightParen,
        LeftBrace,
        RightBrace,

        FatArrow,
        Add,

        Assign,

        Error,
        EndOfStream
    }
}
