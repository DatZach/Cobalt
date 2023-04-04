using Compiler.Parser.Expressions;

namespace Compiler.Parser.Visitors
{
    internal interface IExpressionVisitor<out T>
    {
        T Visit(ScriptExpression expression);
        T Visit(FunctionExpression expression);
        
        T Visit(BinaryOperatorExpression expression);
        T Visit(BlockExpression expression);
        T Visit(CallExpression expression);
        T Visit(IdentifierExpression expression);
        T Visit(NumberExpression expression);
    }
}
