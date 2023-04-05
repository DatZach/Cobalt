using Compiler.Lexer;
using Compiler.Ast.Parselets;
using Compiler.Ast.Parselets.Statements;

namespace Compiler.Ast
{
    internal sealed partial class Parser
    {
        private readonly static Dictionary<TokenType, IPrefixExpressionParselet> ExpressionPrefixParselets;
        private readonly static Dictionary<TokenType, IInfixExpressionParselet> ExpressionInfixParselets;
        private readonly static Dictionary<TokenType, IPrefixStatementParselet> StatementPrefixParselets;

        static Parser()
        {
            ExpressionPrefixParselets = new Dictionary<TokenType, IPrefixExpressionParselet>();
            ExpressionInfixParselets = new Dictionary<TokenType, IInfixExpressionParselet>();
            StatementPrefixParselets = new Dictionary<TokenType, IPrefixStatementParselet>();

            // Primitives
            Register(TokenType.Number, new NumberParselet());
            Register(TokenType.String, new StringParselet());
            Register(TokenType.Identifier, new IdentifierParselet());
            Register(TokenType.LeftParen, new GroupParselet());
            Register(TokenType.LeftParen, new CallParselet());
            Register(TokenType.Function, new FunctionParselet());
            Register(TokenType.Semicolon, new EmptyParselet());

            // Expression Operators
            Register(TokenType.Add, new BinaryOperatorParselet(PrecedenceTable.Addition));

            // Assignments
            Register(TokenType.Assign, new BinaryOperatorParselet(PrecedenceTable.Assignment));

            // Statements
            Register(TokenType.Const, new VarParselet());
            Register(TokenType.Extern, new ExternParselet());
        }

        private static void Register(TokenType type, IPrefixExpressionParselet parselet)
        {
            ExpressionPrefixParselets.Add(type, parselet);
        }

        private static void Register(TokenType type, IInfixExpressionParselet parselet)
        {
            ExpressionInfixParselets.Add(type, parselet);
        }

        private static void Register(TokenType type, IPrefixStatementParselet parselet)
        {
            StatementPrefixParselets.Add(type, parselet);
        }
    }
}
