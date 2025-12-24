using Compiler.Ast.Expressions;
using System.Text;

namespace Compiler.CodeGeneration
{
    internal sealed class TupleType : IContext
    {
        public IContext? Parent { get; }

        public string Name { get; }

        public List<CobField> Fields { get; }

        public List<Function> Functions { get; }

        private readonly Compiler compiler;

        public TupleType(Compiler compiler, IContext? parent, string name)
        {
            this.compiler = compiler;
            Parent = parent;
            Name = name;
            Fields = new List<CobField>(4);
            Functions = new List<Function>();
        }

        public Function AllocateFunction(
            string name,
            CallingConvention callingConvention,
            IReadOnlyList<Function.Parameter> parameters,
            CobType returnType
        ) {
            // TODO Clean this up a little
            callingConvention = CallingConvention.ThisCall;
            var lParameters = new List<Function.Parameter>(parameters);
            lParameters.Insert(0, new Function.Parameter("this", new CobType(eCobType.Tuple, tag: this), false));
            parameters = lParameters;

            // TODO compiler.RootModule is wrong! Should parent to this context...
            var function = new Function(name, this, callingConvention, parameters, returnType);
            Functions.Add(function);
            compiler.Functions.Add(function);

            return function;
        }

        public int AllocateField(string name, CobType type, Expression? getterExpression, Expression? setterExpression)
        {
            // TODO Probably shouldn't silently ignore name conflicts
            var idx = Fields.FindIndex(x => x.Name == name);
            if (idx == -1)
            {
                idx = Fields.Count;
                Fields.Add(new CobField(this, name, type, getterExpression, setterExpression));
            }

            return idx;
        }

        public int FindField(string name)
        {
            return Fields.FindIndex(x => x.Name == name);
        }

        public Storage? GetIdentifier(CodeGeneration.Compiler compiler, IdentifierExpression expression)
        {
            // FIELD
            int idx; // TODO THIS IS SO BAD
            if ((idx = Fields.FindIndex(x => x.Name == expression.Value)) != -1)
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
            //var function = Functions.FirstOrDefault(x => x.Name == expression.Value);
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

            return null;
            //return compiler.CurrentModule.GetIdentifier(compiler, expression);
        }

        public CobVariable? SetIdentifier(CodeGeneration.Compiler compiler, IdentifierExpression expression)
        {
            int idx;
            if ((idx = Fields.FindIndex(x => x.Name == expression.Value)) != -1)
            {
                var field = Fields[idx];
                //compiler.ValidateVariableAccess(field, expression);
                
                var @this = compiler.BinOpLHS == null ? Operand.This : compiler.BinOpLHS.Operand;
                
                compiler.CurrentFunction.Body.Emit(
                    Opcode.SetField,
                    @this,
                    new Operand { Type = OperandType.ImmediateUnsigned, Value = idx },
                    compiler.AssignmentRHS.Operand
                );
                return field;
            }

            return null;
        }
    }

    internal sealed record CobField : CobVariable
    {
        public Expression? GetterExpression { get; }

        public Expression? SetterExpression { get; }

        private readonly IContext parent;

        public CobField(IContext parent, string name, CobType type, Expression? getterExpression, Expression? setterExpression)
            : base(name, type, true)
        {
            this.parent = parent;

            GetterExpression = getterExpression;
            SetterExpression = setterExpression;
        }

        public override bool IsVisibleTo(IContext? context)
        {
            for (var ctx = context; ctx != null; ctx = ctx.Parent)
            {
                if (ctx == parent)
                    return true;
            }

            return Name.Length > 0 && char.IsUpper(Name[0]);
        }

        public override string ToString()
        {
            return base.ToString();
        }
    }
}
