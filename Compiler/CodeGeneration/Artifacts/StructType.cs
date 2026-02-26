using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;

namespace Compiler.CodeGeneration.Artifacts
{
    internal sealed class StructType : IScopeContext, ISymbol
    {
        public string Name { get; }

        public IScopeContext Parent { get; }

        public Indexer? Indexer { get; private set; }

        public bool IsGeneric => generics.Count > 0;

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
            var module = Parent as Module;
            if (module == null)
                throw new NotImplementedException(); // TODO Nested TupleType, StructType

            var concreteName = $"{Name}`{bType}";

            return module.FindStructType(concreteName);
        }

        public StructType AllocateAsConcretizedStruct(CobType bType)
        {
            var module = Parent as Module;
            if (module == null)
                throw new NotImplementedException(); // TODO Nested TupleType, StructType

            var concreteName = $"{Name}`{bType}";

            var concreteStructType = module.AllocateStructType(concreteName);

            foreach (var trait in traits)
            {
                concreteStructType.AttachTrait(trait);
            }

            foreach (var x in factories)
            {
                var parameters = x.Parameters.Select(
                    y => new Function.Parameter(y.Name, y.Type.ToConcreteType(bType), y.IsSpread)
                ).ToList();

                concreteStructType.AllocateFactory(x.Name, parameters);
            }

            foreach (var x in functions)
            {
                var parameters = x.Parameters.Select(
                    y => new Function.Parameter(y.Name, y.Type.ToConcreteType(bType), y.IsSpread)
                ).ToList();
                var returnType = x.ReturnType.ToConcreteType(bType);

                concreteStructType.AllocateFunction(x.Name, parameters, returnType);
            }

            foreach (var x in fields)
            {
                var fieldType = x.Type.ToConcreteType(bType);
                concreteStructType.AllocateField(x.Name, fieldType, x.GetterExpression, x.SetterExpression);
            }

            if (Indexer != null)
            {
                concreteStructType.Indexer = AllocateIndexer(
                    Indexer.KeyType.ToConcreteType(bType),
                    Indexer.ReturnType.ToConcreteType(bType),
                    Indexer.GetterExpression,
                    Indexer.SetterExpression
                );
            }

            return concreteStructType;
        }

        public bool IsVisibleTo(IScopeContext? context) => Compiler.IsSymbolVisible(context, Parent, Name);

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
