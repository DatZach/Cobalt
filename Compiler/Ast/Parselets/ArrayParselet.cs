using Compiler.Ast.Expressions;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets
{
    internal sealed class ArrayParselet : IInfixExpressionParselet
    {
        public int Precedence => PrecedenceTable.ArrayIndexer;

        public Expression Parse(Parser parser, Expression left, Token token)
        {
            var index = parser.ParseExpression();
            var endToken = parser.Take(TokenType.RightSquare);

            return new ArrayExpression(token, endToken, left, index);
        }
    }
}
