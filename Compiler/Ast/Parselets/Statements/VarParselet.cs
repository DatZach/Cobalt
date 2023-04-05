using Compiler.Lexer;
using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;

namespace Compiler.Ast.Parselets.Statements
{
    internal class VarParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var declarations = new List<VarExpression.Declaration>();
            
            do
            {
                var identifier = parser.Take(TokenType.Identifier);
                
                Expression? initializer = null;
                if (parser.MatchAndTakeToken(TokenType.Assign) != null)
                    initializer = parser.ParseExpression(isConditional: true);

                declarations.Add(new VarExpression.Declaration(identifier, initializer));
            } while(parser.Match(TokenType.Comma));

            return new VarExpression(token, declarations);
        }
    }
}
