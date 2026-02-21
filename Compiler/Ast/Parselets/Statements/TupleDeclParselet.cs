using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class TupleDeclParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var name = parser.Take(TokenType.Identifier);
            var generics = new List<GenericDefinition>();
            var fields = new List<FieldDefinition>();
            var functions = new List<FunctionDeclStatement>();
            IndexerDefinition? indexerDefinition = null;

            while (!parser.IsEndOfStream && parser.MatchAndTakeToken(TokenType.Generic) != null)
            {
                var genericName = parser.Take(TokenType.Identifier);
                var constraintTypeName = parser.MatchAndTakeToken(TokenType.Colon) != null
                    ? parser.ParseTypeName()
                    : null;

                generics.Add(new GenericDefinition(genericName.Value, constraintTypeName));
            }

            //if (parser.MatchAndTakeToken(TokenType.LessThan) != null)
            //{
            //    do
            //    {
            //        var genericName = parser.Take(TokenType.Identifier);
            //        var constraintTypeName = parser.MatchAndTakeToken(TokenType.Colon) != null
            //            ? parser.ParseTypeName()
            //            : null;

            //        generics.Add(new GenericDefinition(genericName.Value, constraintTypeName));
            //    } while (parser.MatchAndTakeToken(TokenType.Comma) != null);


            //    parser.Take(TokenType.MoreThan);
            //}

            parser.Take(TokenType.LeftParen);
            while (!parser.IsEndOfStream && parser.MatchAndTakeToken(TokenType.RightParen) == null)
            {
                if (parser.Match(TokenType.Function)) // Function
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

                    StructDeclParselet.ParseGetterSetters(parser, out var getterExpression, out var setterExpression);

                    indexerDefinition = new IndexerDefinition(keyType, returnType, getterExpression, setterExpression);
                }
                else // Field
                {
                    var fieldName = parser.Take(TokenType.Identifier);
                    parser.Take(TokenType.Colon);
                    var fieldTypeName = parser.ParseTypeName();

                    StructDeclParselet.ParseGetterSetters(parser, out var getterExpression, out var setterExpression);

                    fields.Add(new FieldDefinition(fieldName.Value, fieldTypeName, getterExpression, setterExpression));
                }

                parser.MatchAndTakeToken(TokenType.Semicolon);
            }

            return new TupleDeclStatement(token, name.Value, generics, fields, functions, indexerDefinition);
        }
    }
}
