using Compiler.Ast.Expressions;

namespace Compiler.CodeGeneration
{
    internal sealed class TupleType : IContext, ISymbol
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
            var function = new Function(name, compiler, this, callingConvention, parameters, returnType);
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

        public ISymbol? FindIdentifier(string name)
        {
            CobField? field;
            if ((field = Fields.FirstOrDefault(x => x.Name == name)) != null)
                return field;

            Function? function;
            if ((function = Functions.FirstOrDefault(x => x.Name == name)) != null)
                return function;

            return null;
        }

        public Storage? EmitGetIdentifier(ISymbol identifier)
        {
            if (identifier is CobField field)
            {
                var idx = Fields.IndexOf(field);
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

            if (identifier is Function function)
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

        public bool EmitSetIdentifier(ISymbol identifier)
        {
            if (identifier is CobField field)
            {
                var idx = Fields.IndexOf(field);
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

        public bool IsVisibleTo(IContext context) => Compiler.StandardIsSymbolVisibleHeuristic(context, Parent, Name);
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

        public override bool IsVisibleTo(IContext? context) => Compiler.StandardIsSymbolVisibleHeuristic(context, parent, Name);

        public override string ToString()
        {
            return base.ToString();
        }
    }
}
