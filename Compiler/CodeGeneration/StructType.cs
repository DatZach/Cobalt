using Compiler.Ast.Expressions;

namespace Compiler.CodeGeneration
{
    internal sealed class StructType : IScopeContext, ISymbol
    {
        public string Name { get; }

        public IScopeContext Parent { get; }

        public Indexer? Indexer { get; private set; }

        private readonly List<Function> functions;
        private readonly List<CobField> fields;

        private readonly Compiler compiler;

        public StructType(string name, IScopeContext parent, Compiler compiler)
        {
            this.compiler = compiler;
            Parent = parent;
            Name = name;
            fields = new List<CobField>(4);
            functions = new List<Function>();
        }

        public CobVariable ToVariable()
        {
            var type = new CobType(eCobType.Tuple, tag: this);
            var variable = new CobVariable("$struct", type, false)
            {
                StructValue = fields.Select(x => new CobVariable(x.Name, x.Type, true)).ToArray()
            };

            return variable;
        }

        public Function AllocateFunction(
            string name,
            IReadOnlyList<Function.Parameter> parameters,
            CobType returnType
        ) {
            var lParameters = new List<Function.Parameter>();
            lParameters.Add(new Function.Parameter("this", new CobType(eCobType.Struct, tag: this), false));
            lParameters.AddRange(parameters);

            var function = new Function(name, this, compiler, CallingConvention.ThisCall, lParameters, returnType);
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

        public bool IsVisibleTo(IScopeContext? context) => Compiler.IsSymbolVisible(context, Parent, Name);

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
    }

    internal sealed class Indexer : IScopeContext
    {
        public IScopeContext Parent { get; init; }

        public string Name => "indexer";

        public CobType KeyType { get; init; }

        public CobType ReturnType { get; init; }

        public Expression? GetterExpression { get; init; }

        public Expression? SetterExpression { get; init; }

        public Storage? Index { get; set; }

        public ISymbol? FindSymbol(string name)
        {
            if (name == "key")
                return new CobVariable("key", KeyType, false);

            return null;
        }

        public Storage? EmitGetForSymbol(ISymbol symbol)
        {
            if (symbol is CobVariable variable && variable.Name == "key")
                return Index;

            return null;
        }

        public bool EmitSetForSymbol(ISymbol symbol)
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
