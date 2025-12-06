using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.CodeGeneration;
using Compiler.Lexer;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class TupleDefinitionParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var name = parser.Take(TokenType.Identifier);
            var fields = new List<FieldDefinition>();
            var functions = new List<FunctionExpression>();

            parser.Take(TokenType.LeftParen);
            while (parser.MatchAndTakeToken(TokenType.RightParen) == null)
            {
                if (parser.Match(TokenType.Function))
                {
                    // Function decl
                    var expr = (FunctionExpression)parser.ParseStatement();
                    functions.Add(expr);
                }
                else
                {
                    // Field decl
                    var fieldName = parser.Take(TokenType.Identifier);
                    parser.Take(TokenType.Colon);
                    var fieldType = parser.ParseTypeName();

                    StructDefinitionParselet.ParseGetterSetters(parser, out var getterExpression, out var setterExpression);

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
