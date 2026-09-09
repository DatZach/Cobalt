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
                    var fields = new List<string>();
                    do
                    {
                        var identifier = parser.Take(TokenType.Identifier).Value;
                        fields.Add(identifier);
                    } while (parser.MatchAndTakeToken(TokenType.Comma) != null);

                    parser.Take(TokenType.RightParen);
                    parser.Take(TokenType.Assign);

                    var initializer = parser.ParseExpression(isConditional: true);

                    declaration = new VariableDeclStatement.DestructureDeclaration(fields, initializer);
                }
                else // Standard
                {
                    var identifier = parser.Take(TokenType.Identifier).Value;
                    parser.Take(TokenType.Assign);
                    var initializer = parser.ParseExpression(isConditional: true);

                    declaration = new VariableDeclStatement.StandardDeclaration(identifier, initializer);
                }

                declarations.Add(declaration);
            } while(parser.MatchAndTakeToken(TokenType.Comma) != null);

            return new VariableDeclStatement(token, declarations);
        }
    }
}
