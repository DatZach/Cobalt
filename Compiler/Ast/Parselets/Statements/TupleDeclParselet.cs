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
            var fields = new List<FieldDefinition>();
            var functions = new List<FunctionDeclStatement>();

            parser.Take(TokenType.LeftParen);
            while (parser.MatchAndTakeToken(TokenType.RightParen) == null)
            {
                if (parser.Match(TokenType.Function))
                {
                    // Function decl
                    var expr = (FunctionDeclStatement)parser.ParseStatement();
                    functions.Add(expr);
                }
                else
                {
                    // Field decl
                    var fieldName = parser.Take(TokenType.Identifier);
                    parser.Take(TokenType.Colon);
                    var fieldTypeName = parser.ParseTypeName();

                    StructDeclParselet.ParseGetterSetters(parser, out var getterExpression, out var setterExpression);

                    fields.Add(new FieldDefinition(fieldName.Value, fieldTypeName, getterExpression, setterExpression));
                }

                parser.MatchAndTakeToken(TokenType.Semicolon);
            }

            return new TupleDeclStatement(token, name.Value, fields, functions);
        }
    }
}
