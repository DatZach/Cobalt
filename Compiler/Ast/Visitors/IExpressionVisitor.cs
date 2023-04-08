using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;

namespace Compiler.Ast.Visitors
{
    internal interface IExpressionVisitor<out T>
    {
        T Visit(ScriptExpression expression);
        T Visit(VarExpression expression);
        T Visit(ImportExpression expression);
        T Visit(ArtifactExpression expression);
        
        T Visit(FunctionExpression expression);
        T Visit(BinaryOperatorExpression expression);
        T Visit(BlockExpression expression);
        T Visit(CallExpression expression);
        T Visit(IdentifierExpression expression);
        T Visit(NumberExpression expression);
        T Visit(StringExpression expression);
        T Visit(EmptyExpression expression);
    }
}
