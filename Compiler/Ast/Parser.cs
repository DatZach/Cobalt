using System.Runtime.CompilerServices;
using Compiler.Lexer;
using Compiler.Ast.Expressions;

namespace Compiler.Ast
{
    internal sealed partial class Parser
    {
        private int readIndex;

        private readonly IReadOnlyList<Token> tokens;
        
        private Parser(IReadOnlyList<Token> tokens)
        {
            this.tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        }

        public Expression ParseExpression(int precedence = 0, bool isConditional = false)
        {
            var token = Take();

            if (!ExpressionPrefixParselets.TryGetValue(token.Type, out var prefixExpression))
            {
                throw new Exception("Unexpected expression");
            }

            var left = prefixExpression.Parse(this, token);
            while(GetPrecedence(isConditional) > precedence)
            {
                token = Take();

                var infixExpression = ExpressionInfixParselets[token.Type];
                left = infixExpression.Parse(this, left, token);
            }

            return left;
        }

        public Expression ParseStatement(bool takeSemicolon = true)
        {
            Expression result;

            var token = Peek();
            if (StatementPrefixParselets.TryGetValue(token.Type, out var prefixStatementParselet))
                result = prefixStatementParselet.Parse(this, Take());
            else
                result = ParseExpression();

            if (takeSemicolon)
                MatchAndTakeToken(TokenType.Semicolon);

            return result;
        }

        public Expression ParseBlock(bool braceRequired = false)
        {
            var token = braceRequired ? Take(TokenType.LeftBrace) : MatchAndTakeToken(TokenType.LeftBrace);
            if (token != null)
            {
                var expressions = new List<Expression>(16);

                while (!Match(TokenType.RightBrace) && !Match(TokenType.EndOfStream))
                    expressions.Add(ParseBlock());

                var endToken = Take();
                if (endToken == null || endToken.Type != TokenType.RightBrace)
                {
                    throw new Exception("Missing closing brace");
                }

                return new BlockExpression(token, expressions);
            }

            return ParseStatement();
        }
        
        public ScriptExpression ParseScript()
        {
            var expressions = new List<Expression>();

            while(!Match(TokenType.EndOfStream))
                expressions.Add(ParseBlock());

            return new ScriptExpression(Take(), expressions);
        }

        public Token? MatchAndTakeToken(TokenType type)
        {
            var token = Peek();
            if (token.Type == type)
            {
                Take();
                return token;
            }

            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Match(TokenType type)
        {
            return Peek().Type == type;
        }

        public Token Take(TokenType type)
        {
            var token = Take();
            if (token.Type != type)
            {
                throw new Exception($"Expected {type} but found {token} instead.");
            }

            return token;
        }

        public Token Take()
        {
            var token = Peek();
            ++readIndex;

            return token;
        }

        public Token Peek()
        {
            if (readIndex >= tokens.Count)
                return new Token { Type = TokenType.EndOfStream };

            return tokens[readIndex];
        }

        private int GetPrecedence(bool isConditional)
        {
            var type = Peek().Type;

            if (ExpressionInfixParselets.TryGetValue(type, out var infixExpressionParselet))
                return infixExpressionParselet.Precedence;

            return 0;
        }

        public static ScriptExpression Parse(IReadOnlyList<Token> tokens)
        {
            var parser = new Parser(tokens);
            return parser.ParseScript();
        }
    }
}
