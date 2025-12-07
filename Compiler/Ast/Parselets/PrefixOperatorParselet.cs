using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Compiler.Ast.Expressions;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets
{
    internal sealed class PrefixOperatorParselet : IPrefixExpressionParselet
    {
        private readonly int precedence;

        public PrefixOperatorParselet(int precedence)
        {
            this.precedence = precedence;
        }

        public Expression Parse(Parser parser, Token token)
        {
            var right = parser.ParseExpression(precedence);

            return new PrefixOperatorExpression(token, right);
        }
    }
}
