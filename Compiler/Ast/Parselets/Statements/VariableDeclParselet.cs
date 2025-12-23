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
                var identifier = parser.Take(TokenType.Identifier);
                var typeName = parser.MatchAndTakeToken(TokenType.Colon) != null ? parser.ParseTypeName() : null;
                
                Expression? initializer = null;
                if (parser.MatchAndTakeToken(TokenType.Assign) != null)
                    initializer = parser.ParseExpression(isConditional: true);

                declarations.Add(new VariableDeclStatement.Declaration(identifier, typeName, initializer));
            } while(parser.Match(TokenType.Comma));

            return new VariableDeclStatement(token, declarations);
        }
    }
}
