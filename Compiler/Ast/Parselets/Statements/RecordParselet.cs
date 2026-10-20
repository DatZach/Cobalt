using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.CodeGeneration.Artifacts;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class RecordParselet : IPrefixStatementParselet
    {
        // TODO Remove hard instance, should be possible for this to exist with the rest of the parselets...
        private static RecordParselet? instance;
        public static RecordParselet Instance => instance ??= new RecordParselet();

        private static int uniqueId;

        public Expression Parse(Parser parser, Token token)
        {
            // TODO Handle Tuple-specific errors

            var type = token.Type == TokenType.LeftBrace ? eRecordType.Struct : eRecordType.Tuple;

            var generics = new List<GenericDefinition>();
            var mixins = new List<TypeName>();
            var types = new List<TypeDeclStatement>();
            var constants = new List<VariableDeclStatement>();
            var fields = new List<FieldDefinition>();
            var functions = new List<FunctionDeclStatement>();
            var factories = new List<FactoryDeclStatement>();
            var destructors = new List<FunctionDeclStatement>();
            IndexerDefinition? indexerDefinition = null;

            var terminalToken = token.Type == TokenType.LeftBrace ? TokenType.RightBrace : TokenType.RightParen;
            while (!parser.IsEndOfStream && parser.MatchAndTakeToken(terminalToken) == null)
            {
                if (parser.Match(TokenType.Type))
                {
                    var typeToken = parser.Take(TokenType.Type);
                    var isGeneric = parser.MatchAndTakeToken(TokenType.Generic) != null;
                    var typeName = parser.Take(TokenType.Identifier);
                    var constraint = parser.MatchAndTakeToken(TokenType.Assign) != null
                                   ? parser.ParseTypeName()
                                   : null;
                    var refinement = parser.MatchAndTakeToken(TokenType.Colon) != null
                                   ? parser.ParseExpression()
                                   : null;
                    if (isGeneric)
                        generics.Add(new GenericDefinition(typeName.Value, constraint));
                    else
                        types.Add(new TypeDeclStatement(typeToken, typeName.Value, constraint, refinement));
                }
                else if (parser.Match(TokenType.Mixin))
                {
                    parser.Take(TokenType.Mixin);
                    var typeName = parser.ParseTypeName();
                    mixins.Add(typeName);
                }
                else if (parser.Match(TokenType.Const))
                {
                    var expr = (VariableDeclStatement)parser.ParseStatement();
                    constants.Add(expr);
                }
                else if (parser.Match(TokenType.Factory)) // Factory
                {
                    var expr = (FactoryDeclStatement)parser.ParseStatement();
                    factories.Add(expr);
                }
                else if (parser.Match(TokenType.Function)) // Function
                {
                    var expr = (FunctionDeclStatement)parser.ParseStatement();
                    functions.Add(expr);
                }
                else if (parser.Match(TokenType.Indexer)) // Indexer
                {
                    parser.Take(TokenType.Indexer);
                    parser.Take(TokenType.LeftSquare);
                    var keyTypeName = parser.ParseTypeName();
                    parser.Take(TokenType.RightSquare);
                    var returnTypeName = parser.ParseTypeName();

                    ParseGetterSetters(parser, out var getterExpression, out var setterExpression);

                    indexerDefinition = new IndexerDefinition(keyTypeName, returnTypeName, getterExpression, setterExpression);
                }
                else // Field
                {
                    var isFinal = parser.MatchAndTakeToken(TokenType.Final) != null;
                    var fieldType = parser.ParseTypeName();
                    var fieldName = parser.Take(TokenType.Identifier);

                    ParseGetterSetters(parser, out var getterExpression, out var setterExpression);

                    fields.Add(new FieldDefinition(fieldName.Value, fieldType, isFinal, getterExpression, setterExpression));
                }

                parser.MatchAndTakeToken(TokenType.Semicolon);
            }

            var name = $"AnonymousRecord'{uniqueId++}";

            return new StructDeclStatement(
                token, type, name,
                generics, mixins, types, constants, fields,
                functions, factories, destructors, indexerDefinition
            );
        }

        // TODO Move into VariableDeclParselet
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
