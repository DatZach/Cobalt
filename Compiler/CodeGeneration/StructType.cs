using Compiler.Ast.Expressions;

namespace Compiler.CodeGeneration
{
    internal sealed class StructType : IContext
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

    internal sealed class Indexer : IContext
    {
        public IContext? Parent { get; init;  }

        public string Name { get; } = "indexer";

        public CobType KeyType { get; init; }

        public CobType ReturnType { get; init; }

        public Expression? GetterExpression { get; init; }

        public Expression? SetterExpression { get; init; }

        public Storage? Index { get; set; }

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
