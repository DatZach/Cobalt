using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;

namespace Compiler.Ast.Visitors
{
    internal interface IExpressionVisitor<out T>
    {
        T Visit(ScriptExpression expression);
        
        T Visit(ImportStatement expression);
        T Visit(ExportStatement expression);
        T Visit(ArtifactStatement expression);
        T Visit(ModuleStatement expression);
        T Visit(TypeAliasStatement expression);
        T Visit(TraitStatement expression);
        T Visit(ErrorStatement expression);
        T Visit(TupleDeclStatement expression);
        T Visit(StructDeclStatement expression);
        T Visit(FactoryDeclStatement expression);
        T Visit(FunctionDeclStatement expression);
        T Visit(VariableDeclStatement expression);
        
        T Visit(IfStatement expression);
        T Visit(ForStatement expression);
        T Visit(ContinueStatement expression);
        T Visit(BreakStatement expression);
        T Visit(ReturnStatement expression);
        T Visit(MachineStatement expression);
        
        T Visit(BlockExpression expression);
        T Visit(FatArrowStatement expression);
        T Visit(BinaryOperatorExpression expression);
        T Visit(PrefixOperatorExpression expression);
        T Visit(PostfixOperatorExpression expression);
        T Visit(PatternMatchExpression expression);
        T Visit(CallExpression expression);
        T Visit(IdentifierExpression expression);
        T Visit(AheadOfTimeExpression expression);
        T Visit(LensExpression expression);
        T Visit(IndexerExpression expression);

        T Visit(StructLiteralExpression expression);
        T Visit(NumberLiteralExpression expression);
        T Visit(BooleanLiteralExpression expression);
        T Visit(StringLiteralExpression expression);
        T Visit(NilLiteralExpression expression);
        T Visit(EmptyExpression expression);
    }
}
