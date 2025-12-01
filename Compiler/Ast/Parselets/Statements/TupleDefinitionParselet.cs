using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.CodeGeneration;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class TupleDefinitionParselet : IPrefixExpressionParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var name = parser.Take(TokenType.Identifier);
            var fields = new List<FieldDefinition>();
            var functions = new List<Expression>();

            parser.Take(TokenType.LeftParen);
            while (parser.MatchAndTakeToken(TokenType.RightParen) == null)
            {
                if (parser.Match(TokenType.Function))
                {
                    // Function decl
                    var expr = parser.ParseStatement();
                    functions.Add(expr);
                }
                else
                {
                    // Field decl
                    var fieldName = parser.Take(TokenType.Identifier);
                    parser.Take(TokenType.Colon);
                    var fieldType = parser.ParseTypeName();

                    Expression? getterExpression, setterExpression;
                    if (parser.MatchAndTakeToken(TokenType.FatArrow) != null)
                    {
                        getterExpression = parser.ParseExpression();
                        setterExpression = null;
                    }
                    else if (parser.MatchAndTakeToken(TokenType.LeftBrace) != null)
                    {
                        getterExpression = setterExpression = null;
                        while (parser.MatchAndTakeToken(TokenType.RightBrace) != null)
                        {
                            var keyword = parser.Take(TokenType.Identifier);
                            if (keyword.Value == "get")
                            {
                                parser.Take(TokenType.FatArrow);
                                getterExpression = parser.ParseExpression();
                            }
                            else if (keyword.Value == "set")
                            {
                                parser.Take(TokenType.FatArrow);
                                setterExpression = parser.ParseExpression();
                            }
                            else
                                parser.Messages.Add(Message.UnexpectedToken2, keyword, "get or set", keyword.Value);
                        }
                    }
                    else
                    {
                        getterExpression = null;
                        setterExpression = null;
                    }

                    fields.Add(new FieldDefinition(fieldName.Value, fieldType, getterExpression, setterExpression));
                }

                parser.MatchAndTakeToken(TokenType.Semicolon);
            }

            var result = new TupleDefinitionExpression(token, name.Value, fields, functions);

            // TODO HACK Should be done in a Compiler FirstPass, not here
            if (!CobType.TryAddAlias(name.Value, new CobType(eCobType.Tuple, tag: result)))
                parser.Messages.Add(Message.SymbolConflictsWithOther, token, name.Value);

            return result;
        }
    }
}
