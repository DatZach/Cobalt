using Compiler.Ast.Visitors;
using Compiler.CodeGeneration;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.Ast.Expressions.Statements
{
    internal sealed class TupleDeclStatement : Expression, IContext
    {
        public string Name { get; }

        public IReadOnlyList<FieldDefinition> Fields { get; }

        public IReadOnlyList<FunctionDeclStatement> Functions { get; }

        public TupleDeclStatement(
            Token token,
            string name,
            IReadOnlyList<FieldDefinition> fields,
            IReadOnlyList<FunctionDeclStatement> functions
        )
            : base(token)
        {
            Name = name;
            Fields = fields;
            Functions = functions;
        }

        [DebuggerStepThrough]
        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }

        public Storage? GetIdentifier(CodeGeneration.Compiler compiler, IdentifierExpression expression)
        {
            // FIELD
            int idx; // TODO THIS IS SO BAD
            if ((idx = Fields.ToList().FindIndex(x => x.Name == expression.Value)) != -1)
            {
                var field = Fields[idx];
                var fieldType = field.Type;
                

                if (field.GetterExpression != null)
                {
                    return field.GetterExpression.Accept(compiler);
                }
                else
                {
                    var @this = compiler.BinOpLHS == null ? Operand.This : compiler.BinOpLHS.Operand;
                    var storage = compiler.CurrentFunction.AllocateStorage(fieldType);
                    compiler.CurrentFunction.Body.Emit(
                        Opcode.GetField,
                        storage.Operand,
                        @this,
                        new Operand { Type = OperandType.ImmediateUnsigned, Value = idx }
                    );
                    return storage;
                }
            }

            // FUNCTION
            //if (Functions.FirstOrDefault(x => x.Name == expression.Value) != null
            //&& (idx = compiler.FindGlobal(expression.Value)) != -1)
            idx = compiler.Functions.FindIndex(x => x.Name == expression.Value); // TODO AllocateFunction + FindFunctionIndex()
            if (idx != -1)
            {
                // TODO Implement
                //if (!Compiler.IsSymbolVisible(global))
                //{
                //    Compiler.Messages.Add(Message.CannotAccessPrivateSymbol, expression, expression.Value, Compiler.CurrentModule.Name ?? "(root)");
                //    return null;
                //}

                var function = compiler.Functions[idx];
                return new Storage(
                    null,
                    new Operand
                    {
                        Type = OperandType.Function,
                        Value = idx
                    },
                    new CobType(eCobType.Function, tag: function)
                );
            }

            return compiler.CurrentModule.GetIdentifier(compiler, expression);

            //return null;
        }

        public void SetIdentifier(CodeGeneration.Compiler compiler, IdentifierExpression expression)
        {
            int idx;
            if ((idx = Fields.ToList().FindIndex(x => x.Name == expression.Value)) != -1)
            {
                //var field = Fields[idx];
                //compiler.ValidateVariableAccess(field, expression);
                
                var @this = compiler.BinOpLHS == null ? Operand.This : compiler.BinOpLHS.Operand;
                
                compiler.CurrentFunction.Body.Emit(
                    Opcode.SetField,
                    @this,
                    new Operand { Type = OperandType.ImmediateUnsigned, Value = idx },
                    compiler.AssignmentRHS.Operand
                );
                return;
            }

            compiler.ValidateVariableAccess(null, expression);
        }
    }

    internal sealed record FieldDefinition(
        string Name,
        CobType Type,
        Expression? GetterExpression,
        Expression? SetterExpression
    );
}
