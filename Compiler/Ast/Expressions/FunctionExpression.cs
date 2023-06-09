﻿using Compiler.Lexer;
using Compiler.Ast.Visitors;
using Compiler.CodeGeneration;
using System.Diagnostics;

namespace Compiler.Ast.Expressions
{
    internal sealed class FunctionExpression : Expression
    {
        public string Name => $"fn_{Token.Line + 1}_{Token.Column + 1}";
        
        public CobType ReturnType { get; }

        public CallingConvention CallingConvention { get; }

        public IReadOnlyList<Function.Parameter> Parameters { get; }

        public Expression? Body { get; }

        public FunctionExpression(
            Token token,
            IReadOnlyList<Function.Parameter> parameters,
            Expression? body,
            CobType returnType,
            CallingConvention callingConvention
        )
            : base(token)
        {
            Parameters = parameters;
            Body = body;
            ReturnType = returnType;
            CallingConvention = callingConvention;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
