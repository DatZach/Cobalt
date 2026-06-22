using Compiler.Lexer;
using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class VariableDeclParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var declarations = new List<VariableDeclStatement.Declaration>();
            
            do
            {
                VariableDeclStatement.Declaration declaration;
                if (parser.MatchAndTakeToken(TokenType.LeftParen) != null) // Destructure
                {
                    var fields = new List<VariableDeclStatement.DestructureDeclaration.Field>();
                    do
                    {
                        var identifier = parser.Take(TokenType.Identifier).Value;
                        var typeName = parser.MatchAndTakeToken(TokenType.Colon) != null ? parser.ParseTypeName() : null;

                        fields.Add(new VariableDeclStatement.DestructureDeclaration.Field(identifier, typeName));
                    } while (parser.MatchAndTakeToken(TokenType.Comma) != null);

                    parser.Take(TokenType.RightParen);

                    Expression? initializer = null;
                    if (parser.MatchAndTakeToken(TokenType.Assign) != null)
                        initializer = parser.ParseExpression(isConditional: true);

                    declaration = new VariableDeclStatement.DestructureDeclaration(fields, initializer);
                }
                else // Standard
                {
                    var identifier = parser.Take(TokenType.Identifier).Value;
                    var typeName = parser.MatchAndTakeToken(TokenType.Colon) != null ? parser.ParseTypeName() : null;
                
                    Expression? initializer = null;
                    if (parser.MatchAndTakeToken(TokenType.Assign) != null)
                        initializer = parser.ParseExpression(isConditional: true);

                    declaration = new VariableDeclStatement.StandardDeclaration(identifier, typeName, initializer);
                }

                declarations.Add(declaration);
            } while(parser.MatchAndTakeToken(TokenType.Comma) != null);

            return new VariableDeclStatement(token, declarations);
        }
    }
}
