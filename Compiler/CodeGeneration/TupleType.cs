using Compiler.Ast.Expressions;

namespace Compiler.CodeGeneration
{
    internal sealed class TupleType : IScopeContext, ISymbol
    {
        public string Name { get; }

        public IScopeContext Parent { get; }

        private readonly List<CobField> fields;
        private readonly List<Function> functions;

        private readonly Compiler compiler;

        public TupleType(string name, IScopeContext parent, Compiler compiler)
        {
            Name = name;
            Parent = parent;
            this.compiler = compiler;
            
            fields = new List<CobField>(4);
            functions = new List<Function>();
        }

        public CobVariable ToVariable()
        {
            var type = new CobType(eCobType.Tuple, tag: this);
            var variable = new CobVariable("$tuple", type, false)
            {
                StructValue = fields.Select(x => new CobVariable(x.Name, x.Type, true)).ToArray()
            };

            return variable;
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

            var function = new Function(name, compiler, this, callingConvention, parameters, returnType);
            functions.Add(function);
            compiler.Functions.Add(function);

            return function;
        }

        public Function? FindFunction(string name)
        {
            return functions.FirstOrDefault(x => x.Name == name);
        }

        public CobField AllocateField(string name, CobType type, Expression? getterExpression, Expression? setterExpression)
        {
            var field = new CobField(this, name, type, getterExpression, setterExpression);
            fields.Add(field);

            return field;
        }

        public CobField? FindField(string name)
        {
            return fields.FirstOrDefault(x => x.Name == name);
        }

        public ISymbol? FindSymbol(string name)
        {
            CobField? field;
            if ((field = fields.FirstOrDefault(x => x.Name == name)) != null)
                return field;

            Function? function;
            if ((function = functions.FirstOrDefault(x => x.Name == name)) != null)
                return function;

            return null;
        }

        public Storage? EmitGetForSymbol(ISymbol symbol)
        {
            if (symbol is CobField field)
            {
                var idx = fields.IndexOf(field);
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

            if (symbol is Function function)
            {
                var idx = compiler.Functions.IndexOf(function);
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
        }

        public bool EmitSetForSymbol(ISymbol symbol)
        {
            if (symbol is CobField field)
            {
                var idx = fields.IndexOf(field);
                var @this = compiler.BinOpLHS == null ? Operand.This : compiler.BinOpLHS.Operand;
                
                compiler.CurrentFunction.Body.Emit(
                    Opcode.SetField,
                    @this,
                    new Operand { Type = OperandType.ImmediateUnsigned, Value = idx },
                    compiler.AssignmentRHS.Operand
                );
                return true;
            }

            return false;
        }

        public bool IsVisibleTo(IScopeContext context) => Compiler.IsSymbolVisible(context, Parent, Name);
    }

    internal sealed record CobField : CobVariable
    {
        public Expression? GetterExpression { get; }

        public Expression? SetterExpression { get; }

        private readonly IScopeContext parent;

        public CobField(IScopeContext parent, string name, CobType type, Expression? getterExpression, Expression? setterExpression)
            : base(name, type, true)
        {
            this.parent = parent;

            GetterExpression = getterExpression;
            SetterExpression = setterExpression;
        }

        public override bool IsVisibleTo(IScopeContext? context) => Compiler.IsSymbolVisible(context, parent, Name);

        public override string ToString()
        {
            return base.ToString();
        }
    }
}
