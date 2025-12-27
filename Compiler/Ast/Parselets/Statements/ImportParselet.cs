using System.Text;
using Compiler.Lexer;
using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.CodeGeneration;
using Compiler.CodeGeneration.Artifacts;

namespace Compiler.Ast.Parselets.Statements
{
    internal sealed class ImportParselet : IPrefixStatementParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var sourceFile = new StringBuilder(128);
            while (true)
            {
                Token? identToken;
                if ((identToken = parser.MatchAndTakeToken(TokenType.Identifier)) != null)
                    sourceFile.Append(identToken.Value);
                else if ((identToken = parser.MatchAndTakeToken(TokenType.Multiply)) != null)
                    sourceFile.Append(identToken.Value);
                else
                    parser.Messages.Add(Message.UnexpectedToken2, token, "identifier or *", token);

                if ((identToken = parser.MatchAndTakeToken(TokenType.Dot)) != null)
                    sourceFile.Append(identToken.Value);
                else
                    break;
            }

            var symbolName = parser.MatchAndTakeToken(TokenType.Identifier);

            CobType? symbolType;
            FunctionDeclStatement? functionSignature;
            if (symbolName != null)
            {
                if (parser.Match(TokenType.Function))
                {
                    symbolType = CobType.Func;
                    functionSignature = parser.ParseExpression() as FunctionDeclStatement;
                }
                else
                {
                    symbolType = CobType.FromString(parser.ParseTypeName());
                    functionSignature = null;
                }
            }
            else
            {
                symbolType = null;
                functionSignature = null;
            }

            return new ImportStatement(
                token,
                sourceFile.ToString(),
                symbolName?.Value,
                symbolType,
                functionSignature
            );
        }
    }
}
