using Compiler.Ast.Expressions;

namespace Compiler.CodeGeneration
{
    internal sealed class StructType : IContext, ISymbol
    {
        public IContext? Parent { get; }

        public string Name { get; }

        public List<CobField> Fields { get; }

        public List<Function> Functions { get; }

        public Indexer? Indexer { get; private set; }

        private readonly Compiler compiler;

        public StructType(Compiler compiler, IContext? parent, string name)
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
            lParameters.Insert(0, new Function.Parameter("this", new CobType(eCobType.Struct, tag: this), false));
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

        public Indexer AllocateIndexer(CobType keyType, CobType returnType, Expression? getterExpression, Expression? setterExpression)
        {
            // TODO Allocate functions for the getters/setters
            var indexer = new Indexer
            {
                Parent = this,
                KeyType = keyType,
                ReturnType = returnType,
                GetterExpression = getterExpression,
                SetterExpression = setterExpression
            };

            Indexer = indexer;

            return indexer;
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

        public bool IsVisibleTo(IContext? context) => Compiler.StandardIsSymbolVisibleHeuristic(context, Parent, Name);
    }

    internal sealed class Indexer : IContext
    {
        public IContext? Parent { get; init;  }

        public string Name => "indexer";

        public CobType KeyType { get; init; }

        public CobType ReturnType { get; init; }

        public Expression? GetterExpression { get; init; }

        public Expression? SetterExpression { get; init; }

        public Storage? Index { get; set; }

        public ISymbol? FindIdentifier(string name)
        {
            if (name == "key")
                return new CobVariable("key", KeyType, false);

            return null;
        }

        public Storage? EmitGetIdentifier(ISymbol identifier)
        {
            if (identifier is CobVariable variable && variable.Name == "key")
                return Index;

            return null;
        }

        public bool EmitSetIdentifier(ISymbol identifier)
        {
            return false;
        }

        public Storage? GetIdentifier(CodeGeneration.Compiler compiler, IdentifierExpression expression)
        {
            if (expression.Value == "key")
                return Index;

            return null;
        }

        public CobVariable? SetIdentifier(CodeGeneration.Compiler compiler, IdentifierExpression expression)
        {
            return null;
        }
    }
}
