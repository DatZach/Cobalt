using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Compiler.Lexer;
using Compiler.Parser.Expressions;

namespace Compiler.Parser.Parselets
{
    internal sealed class BinaryOperatorParselet : IInfixExpressionParselet
    {
        public int Precedence { get; }

        public BinaryOperatorParselet(int precedence)
        {
            Precedence = precedence;
        }

        public Expression Parse(Parser parser, Expression left, Token token)
        {
            var right = parser.ParseExpression(Precedence, false);
            return new BinaryOperatorExpression(token, left, right);
        }
    }
}
