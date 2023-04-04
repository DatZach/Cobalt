using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Compiler.Lexer;
using Compiler.Parser.Expressions;

namespace Compiler.Parser.Parselets
{
    internal sealed class CallParselet : IInfixExpressionParselet
    {
        public int Precedence => PrecedenceTable.FunctionCall;

        public Expression Parse(Parser parser, Expression left, Token token)
        {
            throw new NotImplementedException();
        }
    }
}
