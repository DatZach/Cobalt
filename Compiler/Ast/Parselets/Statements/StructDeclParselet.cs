using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class StructDeclParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var name = parser.Take(TokenType.Identifier);
            var traits = new List<string>();
            var fields = new List<FieldDefinition>();
            var functions = new List<FunctionDeclStatement>();
            var factories = new List<FactoryDeclStatement>();
            IndexerDefinition? indexerDefinition = null;

            if (parser.MatchAndTakeToken(TokenType.Mixin) != null)
            {
                do
                {
                    var traitTypeName = parser.ParseTypeName();
                    traits.Add(traitTypeName);
                } while (parser.MatchAndTakeToken(TokenType.Comma) != null);
            }

            parser.Take(TokenType.LeftBrace);
            while (parser.MatchAndTakeToken(TokenType.RightBrace) == null)
            {
                if (parser.Match(TokenType.Factory)) // Factory
                {
                    var expr = (FactoryDeclStatement)parser.ParseStatement();
                    factories.Add(expr);
                }
                else if (parser.Match(TokenType.Function)) // Function
                {
                    var expr = (FunctionDeclStatement)parser.ParseStatement();
                    functions.Add(expr);
                }
                else if (parser.Match(TokenType.LeftSquare)) // Indexer
                {
                    parser.Take(TokenType.LeftSquare);
                    var keyType = parser.ParseTypeName();
                    parser.Take(TokenType.RightSquare);
                    parser.Take(TokenType.Colon);
                    var returnType = parser.ParseTypeName();

                    ParseGetterSetters(parser, out var getterExpression, out var setterExpression);

                    indexerDefinition = new IndexerDefinition(keyType, returnType, getterExpression, setterExpression);
                }
                else // Field
                {
                    var fieldName = parser.Take(TokenType.Identifier);
                    parser.Take(TokenType.Colon);
                    var fieldType = parser.ParseTypeName();

                    ParseGetterSetters(parser, out var getterExpression, out var setterExpression);

                    fields.Add(new FieldDefinition(fieldName.Value, fieldType, getterExpression, setterExpression));
                }

                parser.MatchAndTakeToken(TokenType.Semicolon);
            }

            return new StructDeclStatement(token, name.Value, traits, fields, functions, factories, indexerDefinition);
        }

        public static void ParseGetterSetters(
            Parser parser,
            out Expression? getterExpression,
            out Expression? setterExpression
        ) {
            if (parser.MatchAndTakeToken(TokenType.FatArrow) != null)
            {
                getterExpression = parser.ParseExpression();
                setterExpression = null;
            }
            else if (parser.MatchAndTakeToken(TokenType.LeftBrace) != null)
            {
                getterExpression = setterExpression = null;
                while (parser.MatchAndTakeToken(TokenType.RightBrace) == null)
                {
                    var keyword = parser.Take(TokenType.Identifier);
                    if (keyword.Value == "get")
                    {
                        getterExpression = parser.MatchAndTakeToken(TokenType.FatArrow) != null
                                         ? parser.ParseExpression()
                                         : parser.ParseBlock(true);
                    }
                    else if (keyword.Value == "set")
                    {
                        setterExpression = parser.MatchAndTakeToken(TokenType.FatArrow) != null
                                         ? parser.ParseExpression()
                                         : parser.ParseBlock(true);
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
        }
    }
}
