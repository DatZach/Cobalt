using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Compiler.Lexer;
using Compiler.Parser.Parselets;

namespace Compiler.Parser
{
    internal sealed partial class Parser
    {
        private static readonly Dictionary<TokenType, IPrefixExpressionParselet> ExpressionPrefixParselets;
        private static readonly Dictionary<TokenType, IInfixExpressionParselet> ExpressionInfixParselets;

        static Parser()
        {
            ExpressionPrefixParselets = new Dictionary<TokenType, IPrefixExpressionParselet>();
            ExpressionInfixParselets = new Dictionary<TokenType, IInfixExpressionParselet>();

            // Primitives
            Register(TokenType.Number, new NumberParselet());
            Register(TokenType.Identifier, new IdentifierParselet());
            Register(TokenType.LeftParen, new GroupParselet());
            Register(TokenType.LeftParen, new CallParselet());
            Register(TokenType.Semicolon, new EmptyParselet());

            // Expression Operators
            Register(TokenType.Add, new BinaryOperatorParselet(PrecedenceTable.Addition));

            // Assignments
            Register(TokenType.Assign, new BinaryOperatorParselet(PrecedenceTable.Assignment));
        }

        private static void Register(TokenType type, IPrefixExpressionParselet expressionParselet)
        {
            ExpressionPrefixParselets.Add(type, expressionParselet);
        }

        private static void Register(TokenType type, IInfixExpressionParselet expressionParselet)
        {
            ExpressionInfixParselets.Add(type, expressionParselet);
        }
    }
}
