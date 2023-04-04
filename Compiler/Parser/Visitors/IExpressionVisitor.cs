using Compiler.Parser.Expressions;
using Compiler.Parser.Expressions.Statements;

namespace Compiler.Parser.Visitors
{
    internal interface IExpressionVisitor<out T>
    {
        T Visit(ScriptExpression expression);
        T Visit(VarExpression expression);
        T Visit(ExternExpression expression);
        
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
