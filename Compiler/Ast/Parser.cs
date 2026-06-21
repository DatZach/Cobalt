using System.Diagnostics;
using System.Runtime.CompilerServices;
using Compiler.Ast.Expressions;
using Compiler.CodeGeneration.Artifacts;
using Compiler.Lexer;

namespace Compiler.Ast
{
    internal sealed partial class Parser
    {
        public MessageCollection Messages { get; }

        public bool IsEndOfStream => Peek().Type == TokenType.EndOfStream;

        private int readIndex;

        private readonly IReadOnlyList<Token> tokens;

        private Parser(IReadOnlyList<Token> tokens, MessageCollection messages)
        {
            this.tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            Messages = messages ?? throw new ArgumentNullException(nameof(messages));
        }

        public Expression ParseExpression(int precedence = 0, bool isConditional = false, bool allowEmpty = false)
        {
            var token = Peek();

            if (!ExpressionPrefixParselets.TryGetValue(token.Type, out var prefixExpression))
            {
                if (!allowEmpty)
                    Messages.Add(Message.UnexpectedToken1, token, token.Type.GetDescription());

                return new EmptyExpression(token);
            }

            Take();

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
                if (endToken.Type != TokenType.RightBrace)
                    Messages.Add(Message.MissingClosingBrace, token);

                return new BlockExpression(token, expressions);
            }

            return ParseStatement();
        }
        
        public ScriptExpression ParseScript()
        {
            var expressions = new List<Expression>();

            Expression? expr = null;
            while (!Match(TokenType.EndOfStream) && expr is not EmptyExpression)
            {
                expr = ParseBlock();
                expressions.Add(expr);
            }

            return new ScriptExpression(Take(), expressions);
        }

        public TypeName? ParseTypeName()
        {
            // TODO Don't throw exceptions, log errors

            eTypeName type;
            string? identifier = null;
            TypeName.FunctionSignature? function = null;
            TypeName.RecordSignature? record = null;
            TypeName? union = null;
            List<TypeName>? generic = null;
            bool isArray = false;
            bool isErrorable = false;
            bool isNillable = false;

            // Prefix
            if (Match(TokenType.Generic))
            {
                generic = new List<TypeName>();
                while (MatchAndTakeToken(TokenType.Generic) != null)
                {
                    generic.Add(new TypeName { Type = eTypeName.Identifier, Identifier = Take(TokenType.Identifier).Value });
                }
            }

            // Body
            if (Match(TokenType.Identifier) || Match(TokenType.Error) || Match(TokenType.Nil))
            {
                type = eTypeName.Identifier;
                identifier = Take().Value;

                if (Match(TokenType.Generic))
                {
                    if (generic != null)
                        throw new Exception("Illegal type name");

                    generic = new List<TypeName>();
                    while (MatchAndTakeToken(TokenType.Generic) != null)
                        generic.Add(new TypeName { Type = eTypeName.Identifier, Identifier = Take(TokenType.Identifier).Value });
                }
            }
            else if (Match(TokenType.Function))
            {
                var parameters = new List<TypeName.FunctionSignature.Parameter>();

                Take(TokenType.Function);
                Take(TokenType.LeftParen);
                while (!Match(TokenType.RightParen))
                {
                    var isSpread = MatchAndTakeToken(TokenType.Spread) != null;
                    var parameterName = Take(TokenType.Identifier).Value;
                    TypeName parameterTypeName;
                    if (MatchAndTakeToken(TokenType.Colon) != null)
                        parameterTypeName = ParseTypeName() ?? throw new Exception("Illegal parameter type");
                    else
                        parameterTypeName = TypeName.Any;

                    parameters.Add(new TypeName.FunctionSignature.Parameter
                    {
                        Name = parameterName,
                        TypeName = parameterTypeName,
                        IsSpread = isSpread
                    });

                    if (MatchAndTakeToken(TokenType.Comma) == null)
                        break;
                }
                Take(TokenType.RightParen);

                var returnType = ParseTypeName();

                CallingConvention callingConvention;
                if (MatchAndTakeToken(TokenType.CCall) != null)
                    callingConvention = CallingConvention.CCall;
                else if (MatchAndTakeToken(TokenType.StdCall) != null)
                    callingConvention = CallingConvention.StdCall;
                else if (MatchAndTakeToken(TokenType.NakedCall) != null)
                    callingConvention = CallingConvention.NakedCall;
                else
                    callingConvention = CallingConvention.Default;

                type = eTypeName.FunctionSignature;
                function = new TypeName.FunctionSignature
                {
                    Parameters = parameters,
                    ReturnTypeName = returnType,
                    CallingConvention = callingConvention
                };
            }
            else if (Match(TokenType.LeftParen))
            {
                Take(TokenType.LeftParen);
                var fields = new List<TypeName.RecordSignature.Field>();

                while (!Match(TokenType.RightParen))
                {
                    string? fieldName;
                    TypeName fieldTypeName;
                    if (Peek(1).Type == TokenType.Colon)
                    {
                        fieldName = Take(TokenType.Identifier).Value;
                        Take(TokenType.Colon);
                        fieldTypeName = ParseTypeName() ?? throw new Exception("Illegal field type");
                    }
                    else
                    {
                        fieldName = null;
                        fieldTypeName = ParseTypeName() ?? throw new Exception("Illegal field type");
                    }

                    Take(TokenType.Semicolon);

                    fields.Add(new TypeName.RecordSignature.Field
                    {
                        Name = fieldName,
                        TypeName = fieldTypeName
                    });
                }
                Take(TokenType.RightParen);

                type = eTypeName.RecordSignature;
                record = new TypeName.RecordSignature
                {
                    Type = eRecordType.Tuple,
                    Fields = fields
                };
            }
            else if (Match(TokenType.LeftBrace))
            {
                throw new NotImplementedException();
            }
            else
                return null;

            // Suffixes
            if (Match(TokenType.LeftSquare))
            {
                Take(TokenType.LeftSquare);
                Take(TokenType.RightSquare);
                isArray = true;
            }

            if (Match(TokenType.NilErrorCoalesce))
            {
                Take(TokenType.NilErrorCoalesce);
                isNillable = true;
                isErrorable = true;
            }

            if (Match(TokenType.Question))
            {
                Take(TokenType.Question);
                isNillable = true;
            }

            if (Match(TokenType.Not))
            {
                Take(TokenType.Not);
                isErrorable = true;
            }

            if (Match(TokenType.BitOr))
            {
                Take(TokenType.BitOr);
                union = ParseTypeName();
            }

            return new TypeName
            {
                Type = type,
                Identifier = identifier,
                Function = function,
                Record = record,
                Union = union,
                Generic = generic,
                IsArray = isArray,
                IsErrorable = isErrorable,
                IsNillable = isNillable
            };
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
                Messages.Add(Message.UnexpectedToken2, token, type.GetDescription(), token);

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
                return Token.EndOfStream;

            return tokens[readIndex];
        }

        public Token Peek(int offset)
        {
            if (readIndex + offset >= tokens.Count)
                return Token.EndOfStream;

            return tokens[readIndex + offset];
        }

        private int GetPrecedence(bool isConditional)
        {
            var type = Peek().Type;

            if (ExpressionInfixParselets.TryGetValue(type, out var infixExpressionParselet))
                return infixExpressionParselet.Precedence;

            return 0;
        }

        public static ScriptExpression Parse(IReadOnlyList<Token> tokens, MessageCollection messages)
        {
            try
            {
                stopwatch.Start();
                var parser = new Parser(tokens, messages);
                return parser.ParseScript();
            }
            finally
            {
                stopwatch.Stop();
            }
        }

        public static long TotalMilliseconds => stopwatch.ElapsedMilliseconds;
        private static readonly Stopwatch stopwatch = new ();
    }
}
