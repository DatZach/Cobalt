using Compiler.Ast.Expressions;
using Compiler.Lexer;
using Compiler.Ast.Parselets;
using Compiler.Ast.Parselets.Statements;

namespace Compiler.Ast
{
    internal sealed partial class Parser
    {
        private readonly static Dictionary<TokenType, IPrefixExpressionParselet> ExpressionPrefixParselets;
        private readonly static Dictionary<TokenType, IInfixExpressionParselet> ExpressionInfixParselets;
        private readonly static Dictionary<TokenType, IPrefixStatementParselet> StatementPrefixParselets;

        static Parser()
        {
            ExpressionPrefixParselets = new Dictionary<TokenType, IPrefixExpressionParselet>();
            ExpressionInfixParselets = new Dictionary<TokenType, IInfixExpressionParselet>();
            StatementPrefixParselets = new Dictionary<TokenType, IPrefixStatementParselet>();

            // Primitives
            Register(TokenType.Number, new NumberLiteralParselet());
            Register(TokenType.String, new StringLiteralParselet());
            Register(TokenType.True, new BooleanLiteralParselet());
            Register(TokenType.False, new BooleanLiteralParselet());
            Register(TokenType.Nil, new NilLiteralParselet());
            Register(TokenType.Identifier, new IdentifierParselet());
            Register(TokenType.LeftParen, new GroupParselet());
            Register(TokenType.LeftParen, new CallParselet());
            Register(TokenType.LeftBrace, new StructLiteralParselet());
            Register(TokenType.LeftSquare, new IndexerParselet());
            Register(TokenType.Function, new FunctionDeclParselet());
            Register(TokenType.Semicolon, new EmptyParselet());

            Register(TokenType.Range, new BinaryOperatorParselet(PrecedenceTable.Range));
            Register(TokenType.RangeInclusive, new BinaryOperatorParselet(PrecedenceTable.Range));
            Register(TokenType.RangeLength, new BinaryOperatorParselet(PrecedenceTable.Range));
            Register(TokenType.RangeTerminal, new BinaryOperatorParselet(PrecedenceTable.Range));
            Register(TokenType.Range, new PrefixOperatorParselet(PrecedenceTable.Range));
            Register(TokenType.RangeInclusive, new PrefixOperatorParselet(PrecedenceTable.Range));
            Register(TokenType.RangeTerminal, new PrefixOperatorParselet(PrecedenceTable.Range));

            // Expression Operators
            Register(TokenType.Add, new BinaryOperatorParselet(PrecedenceTable.Addition));
            Register(TokenType.Subtract, new BinaryOperatorParselet(PrecedenceTable.Subtraction));
            Register(TokenType.Multiply, new BinaryOperatorParselet(PrecedenceTable.Multiplication));
            Register(TokenType.Exponent, new BinaryOperatorParselet(PrecedenceTable.Exponent));
            Register(TokenType.Divide, new BinaryOperatorParselet(PrecedenceTable.Division));
            Register(TokenType.DivideCeil, new BinaryOperatorParselet(PrecedenceTable.Division));
            Register(TokenType.DivideFloor, new BinaryOperatorParselet(PrecedenceTable.Division));
            Register(TokenType.Remainder, new BinaryOperatorParselet(PrecedenceTable.Modulo));
            Register(TokenType.Modulo, new BinaryOperatorParselet(PrecedenceTable.Modulo));
            Register(TokenType.BitLeftShift, new BinaryOperatorParselet(PrecedenceTable.BitLeftShift));
            Register(TokenType.BitRightShift, new BinaryOperatorParselet(PrecedenceTable.BitRightShift));
            Register(TokenType.BitLeftRotate, new BinaryOperatorParselet(PrecedenceTable.BitLeftShift));
            Register(TokenType.BitRightRotate, new BinaryOperatorParselet(PrecedenceTable.BitRightShift));
            Register(TokenType.BitPack, new BinaryOperatorParselet(PrecedenceTable.BitPack));
            Register(TokenType.BitAnd, new BinaryOperatorParselet(PrecedenceTable.BitAnd));
            Register(TokenType.BitOr, new BinaryOperatorParselet(PrecedenceTable.BitOr));
            Register(TokenType.BitXor, new BinaryOperatorParselet(PrecedenceTable.BitXor));
            Register(TokenType.Not, new PrefixOperatorParselet(PrecedenceTable.Unary));
            Register(TokenType.Not, new PostfixOperatorParselet(PrecedenceTable.ErrorPropagate));
            Register(TokenType.Dot, new BinaryOperatorParselet(PrecedenceTable.Dereference));
            Register(TokenType.ErrorCoalesce, new BinaryOperatorParselet(PrecedenceTable.Coalesce));
            Register(TokenType.NilCoalesce, new BinaryOperatorParselet(PrecedenceTable.Coalesce));
            Register(TokenType.NilErrorCoalesce, new BinaryOperatorParselet(PrecedenceTable.Coalesce));
            Register(TokenType.Subtract, new PrefixOperatorParselet(PrecedenceTable.Unary));

            Register(TokenType.Generic, new BinaryOperatorParselet(PrecedenceTable.GenericType));

            Register(TokenType.Is, new PatternMatchParselet());
            Register(TokenType.In, new BinaryOperatorParselet(PrecedenceTable.In));
            
            Register(TokenType.Equals, new BinaryOperatorParselet(PrecedenceTable.Equals));
            Register(TokenType.NotEquals, new BinaryOperatorParselet(PrecedenceTable.NotEquals));
            Register(TokenType.LessThan, new BinaryOperatorParselet(PrecedenceTable.LessThan));
            Register(TokenType.LessThanOrEquals, new BinaryOperatorParselet(PrecedenceTable.LessThanOrEqual));
            Register(TokenType.MoreThan, new BinaryOperatorParselet(PrecedenceTable.MoreThan));
            Register(TokenType.MoreThanOrEquals, new BinaryOperatorParselet(PrecedenceTable.MoreThanOrEqual));
            Register(TokenType.ConditionalAnd, new BinaryOperatorParselet(PrecedenceTable.ConditionalAnd));
            Register(TokenType.ConditionalOr, new BinaryOperatorParselet(PrecedenceTable.ConditionalOr));

            // Assignments
            Register(TokenType.Assign, new BinaryOperatorParselet(PrecedenceTable.Assignment));
            Register(TokenType.AddAssign, new BinaryOperatorParselet(PrecedenceTable.Assignment));
            Register(TokenType.SubtractAssign, new BinaryOperatorParselet(PrecedenceTable.Assignment));
            Register(TokenType.MultiplyAssign, new BinaryOperatorParselet(PrecedenceTable.Assignment));
            Register(TokenType.DivideAssign, new BinaryOperatorParselet(PrecedenceTable.Assignment));
            Register(TokenType.DivideCeilAssign, new BinaryOperatorParselet(PrecedenceTable.Assignment));
            Register(TokenType.DivideFloorAssign, new BinaryOperatorParselet(PrecedenceTable.Assignment));
            Register(TokenType.RemainderAssign, new BinaryOperatorParselet(PrecedenceTable.Assignment));
            Register(TokenType.ModuloAssign, new BinaryOperatorParselet(PrecedenceTable.Assignment));
            Register(TokenType.BitLeftShiftAssign, new BinaryOperatorParselet(PrecedenceTable.Assignment));
            Register(TokenType.BitRightShiftAssign, new BinaryOperatorParselet(PrecedenceTable.Assignment));
            Register(TokenType.BitLeftRotateAssign, new BinaryOperatorParselet(PrecedenceTable.Assignment));
            Register(TokenType.BitRightRotateAssign, new BinaryOperatorParselet(PrecedenceTable.Assignment));
            Register(TokenType.BitAndAssign, new BinaryOperatorParselet(PrecedenceTable.Assignment));
            Register(TokenType.BitOrAssign, new BinaryOperatorParselet(PrecedenceTable.Assignment));
            Register(TokenType.BitXorAssign, new BinaryOperatorParselet(PrecedenceTable.Assignment));

            // Statements
            Register(TokenType.Artifact, new ArtifactParselet());
            Register(TokenType.Module, new ModuleParselet());
            Register(TokenType.Type, new TypeAliasParselet());
            Register(TokenType.Error, new ErrorParselet());
            Register(TokenType.Error, new IdentifierParselet());
            Register(TokenType.Trait, new TraitParselet());
            Register(TokenType.Tuple, new TupleDeclParselet());
            Register(TokenType.Struct, new StructDeclParselet());
            Register(TokenType.Factory, new FactoryDeclParselet());
            Register(TokenType.Const, new VariableDeclParselet());
            Register(TokenType.Var, new VariableDeclParselet());
            Register(TokenType.Import, new ImportParselet());
            Register(TokenType.Export, new ExportParselet());
            Register(TokenType.Return, new ReturnParselet());
            Register(TokenType.AheadOfTime, new AheadOfTimeParselet());
            Register(TokenType.Lens, new LensParselet());
            Register(TokenType.FatArrow, new FatArrowParselet());

            Register(TokenType.If, new IfParselet());
            Register(TokenType.For, new ForParselet());
            Register(TokenType.Continue, new ContinueParselet());
            Register(TokenType.Break, new BreakParselet());
        }

        private static void Register(TokenType type, IPrefixExpressionParselet parselet)
        {
            ExpressionPrefixParselets.Add(type, parselet);
        }

        private static void Register(TokenType type, IInfixExpressionParselet parselet)
        {
            ExpressionInfixParselets.Add(type, parselet);
        }

        private static void Register(TokenType type, IPrefixStatementParselet parselet)
        {
            StatementPrefixParselets.Add(type, parselet);
        }

        public static bool IsStatementExpression(Expression? expression)
        {
            if (expression == null)
                return false;

            return StatementPrefixParselets.ContainsKey(expression.Token.Type)
                && expression.Token.Type != TokenType.Return;
        }
    }
}
