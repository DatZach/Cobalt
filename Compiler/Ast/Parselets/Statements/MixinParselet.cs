using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class MixinParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var targetTypeName = parser.Take(TokenType.Identifier);

            FunctionDeclStatement? function;
            FieldDefinition? field;
            if (parser.Match(TokenType.Trait))
            {
                throw new NotImplementedException();
            }
            else if (parser.Match(TokenType.Function))
            {
                function = (FunctionDeclStatement)parser.ParseStatement();
                field = null;
            }
            else
            {
                var fieldName = parser.Take(TokenType.Identifier);
                parser.Take(TokenType.Colon);
                var fieldTypeName = parser.ParseTypeName();

                StructDeclParselet.ParseGetterSetters(parser, out var getterExpression, out var setterExpression);

                function = null;
                field = new FieldDefinition(fieldName.Value, fieldTypeName, getterExpression, setterExpression);
            }

            return new MixinDeclStatement(token, targetTypeName.Value, function, field);
        }
    }
}
