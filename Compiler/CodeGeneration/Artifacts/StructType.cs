using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using System.Diagnostics;

namespace Compiler.CodeGeneration.Artifacts
{
    [DebuggerDisplay("Struct '{Name}'")]
    internal sealed class StructType : IScopeContext, ISymbol
    {
        public string Name { get; }

        public IScopeContext Parent { get; }

        public Indexer? Indexer { get; private set; }

        public bool IsGeneric => generics.Count > 0;

        private StructType? pendingSuperType;
        private CobType? pendingBType;

        private readonly List<GenericDefinition> generics;
        private readonly List<TraitType> traits;
        private readonly List<Function> factories;
        private readonly List<Function> functions;
        private readonly List<Field> fields;

        private readonly Compiler compiler;

        public StructType(string name, IScopeContext parent, Compiler compiler)
        {
            Name = name;
            Parent = parent;
            this.compiler = compiler;

            generics = new List<GenericDefinition>();
            traits = new List<TraitType>();
            fields = new List<Field>(4);
            factories = new List<Function>(4);
            functions = new List<Function>(4);
        }

        public Variable ToVariable()
        {
            var type = new CobType(eCobType.Tuple, tag: this);
            var variable = new Variable("$struct", type, false)
            {
                StructValue = fields.Select(x => new Variable(x.Name, x.Type)).ToArray()
            };

            return variable;
        }

        public void AttachTrait(TraitType traitType)
        {
            traits.Add(traitType);
        }

        public TraitType? FindTrait(string name)
        {
            return traits.FirstOrDefault(x => x.Name == name);
        }

        public bool HasTrait(TraitType? traitType)
        {
            return traitType != null && traits.Contains(traitType);
        }

        public Function AllocateFactory(string name, IReadOnlyList<Function.Parameter> parameters)
        {
            var returnType = new CobType(eCobType.Struct, tag: this);
            var function = new Function(name, this, compiler, CallingConvention.Default, parameters, returnType);
            factories.Add(function);
            compiler.Functions.Add(function);

            return function;
        }

        public Function? FindFactory(string name)
        {
            return factories.FirstOrDefault(x => x.Name == name);
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

        public Function? FindVirtualFunction(Function virtFunc)
        {
            var key = virtFunc.Name;
            var result = FindFunction(key);
            return result;
        }

        public Field AllocateField(string name, CobType type, Expression? getterExpression, Expression? setterExpression)
        {
            var field = new Field(this, name, type, getterExpression, setterExpression);
            fields.Add(field);

            return field;
        }

        public Field? FindField(string name)
        {
            return fields.FirstOrDefault(x => x.Name == name);
        }

        public Indexer AllocateIndexer(CobType keyType, CobType returnType)
        {
            var getter = AllocateFunction("$Indexer_$get", new [] { new Function.Parameter("key", keyType, false) }, returnType);
            var setter = AllocateFunction("$Indexer_$set", new [] { new Function.Parameter("key", keyType, false), new Function.Parameter("value", returnType, false) }, returnType);

            var indexer = new Indexer
            {
                Parent = this,
                KeyType = keyType,
                ReturnType = returnType,
                Getter = getter,
                Setter = setter
            };

            Indexer = indexer;

            return indexer;
        }

        public void AttachGeneric(GenericDefinition generic)
        {
            generics.Add(generic);
        }

        public CobType? FindGenericType(string name)
        {
            var generic = generics.FirstOrDefault(x => x.Name == name);
            if (generic == null)
                return null;

            return new CobType(eCobType.Generic);
        }

        public StructType? FindConcretizedStruct(CobType bType)
        {
            if (bType == eCobType.Generic)
                return this;

            var module = Parent as Module;
            if (module == null)
                throw new NotImplementedException(); // TODO Nested TupleType, StructType

            var concreteName = $"{Name}`{bType}";

            return module.FindStructType(concreteName);
        }

        public StructType AllocateConcretizedStruct(CobType bType)
        {
            var module = Parent as Module;
            if (module == null)
                throw new NotImplementedException(); // TODO Nested TupleType, StructType

            var concreteName = $"{Name}`{bType}";

            var concreteStructType = module.AllocateStructType(concreteName);

            // HACK Really should be able to do this in a single pass when algebraic type resolution is implemented
            concreteStructType.pendingSuperType = this;
            concreteStructType.pendingBType = bType;

            return concreteStructType;
        }

        public bool PopulateConcretizedStructIfRequired()
        {
            if (pendingSuperType == null || pendingBType == null)
                return false;

            foreach (var trait in pendingSuperType.traits)
            {
                AttachTrait(trait);
            }

            foreach (var x in pendingSuperType.factories)
            {
                var parameters = x.Parameters.Select(
                    y => new Function.Parameter(y.Name, y.Type.ToConcreteType(pendingBType), y.IsSpread)
                ).ToList();

                AllocateFactory(x.Name, parameters);
            }

            foreach (var x in pendingSuperType.functions)
            {
                var parameters = x.Parameters.Select(
                    y => new Function.Parameter(y.Name, y.Type.ToConcreteType(pendingBType), y.IsSpread)
                ).ToList();
                var returnType = x.ReturnType.ToConcreteType(pendingBType);

                AllocateFunction(x.Name, parameters, returnType);
            }

            foreach (var x in pendingSuperType.fields)
            {
                var fieldType = x.Type.ToConcreteType(pendingBType);
                AllocateField(x.Name, fieldType, x.GetterExpression, x.SetterExpression);
            }

            if (pendingSuperType.Indexer != null)
            {
                Indexer = AllocateIndexer(
                    pendingSuperType.Indexer.KeyType.ToConcreteType(pendingBType),
                    pendingSuperType.Indexer.ReturnType.ToConcreteType(pendingBType)
                );
            }

            pendingSuperType = null;
            pendingBType = null;

            return true;
        }

        public ISymbol? FindSymbol(string name)
        {
            if (name == "This")
                return this;

            Field? field;
            if ((field = FindField(name)) != null)
                return field;

            Function? factory;
            if ((factory = FindFactory(name)) != null)
                return factory;

            Function? function;
            if ((function = FindFunction(name)) != null)
                return function;

            return null;
        }

        public Storage? EmitGetForSymbol(ISymbol symbol)
        {
            if (symbol == this)
            {
                return new Storage(
                    new CobType(eCobType.Struct, tag: this),
                    Operand.None
                );
            }

            if (symbol is Field field)
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
                    var storage = compiler.CurrentFunction.AllocateRegisterStorage(fieldType);
                    compiler.CurrentFunction.Body.Emit(
                        Opcode.GetField,
                        storage.Operand,
                        @this,
                        Operand.ImmediateUnsigned(idx)
                    );
                    return storage;
                }
            }

            if (symbol is Function function)
            {
                var idx = compiler.Functions.IndexOf(function);
                return new Storage(
                    new CobType(eCobType.Function, tag: function),
                    Operand.Function(idx)
                );
            }

            return null;
        }

        public bool EmitSetForSymbol(ISymbol symbol)
        {
            if (symbol is Field field)
            {
                var idx = fields.IndexOf(field);
                var @this = compiler.BinOpLHS == null ? Operand.This : compiler.BinOpLHS.Operand;
                
                compiler.CurrentFunction.Body.Emit(
                    Opcode.SetField,
                    @this,
                    Operand.ImmediateUnsigned(idx),
                    compiler.AssignmentRHS.Operand
                );
                return true;
            }

            return false;
        }

        public bool IsVisibleTo(IScopeContext? context) => Compiler.IsSymbolVisible(context, Parent, Name);

        public override string ToString() => Name;
    }

    internal sealed class Indexer : IScopeContext
    {
        public IScopeContext Parent { get; init; }

        public string Name => "indexer";

        public CobType KeyType { get; init; }

        public CobType ReturnType { get; init; }

        public Function? Getter { get; init; }

        public Function? Setter { get; init; }

        //public Expression? GetterExpression { get; init; }

        //public Expression? SetterExpression { get; init; }

        public Storage? Index { get; set; }

        public ISymbol? FindSymbol(string name)
        {
            if (name == "key")
                return new Variable("key", KeyType, false);

            return null;
        }

        public Storage? EmitGetForSymbol(ISymbol symbol)
        {
            if (symbol is Variable variable && variable.Name == "key")
                return Index;

            return null;
        }

        public bool EmitSetForSymbol(ISymbol symbol)
        {
            return false;
        }

        public Storage? GetIdentifier(Compiler compiler, IdentifierExpression expression)
        {
            if (expression.Value == "key")
                return Index;

            return null;
        }

        public Variable? SetIdentifier(Compiler compiler, IdentifierExpression expression)
        {
            return null;
        }
    }
}
