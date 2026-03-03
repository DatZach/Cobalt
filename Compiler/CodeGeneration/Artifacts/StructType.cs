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

        public StructType? HACK_PendingSuperType => pendingSuperType;

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

        public FunctionCandidates? FindFunctionCandidates(string name)
        {
            var candidates = functions.Where(x => x.Name == name).ToList();
            return candidates.Count > 0
                 ? new FunctionCandidates(candidates)
                 : null;
        }

        public Field AllocateField(string name, CobType type, bool hasGetter, bool hasSetter)
        {
            var getter = hasGetter ? AllocateFunction($"{name}_$get", Array.Empty<Function.Parameter>(), type) : null;
            var setter = hasSetter ? AllocateFunction($"{name}_$set", new [] { new Function.Parameter("value", type, false) }, type) : null;

            var field = new Field(this, name, type, getter, setter);
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
                var parameters = x.Parameters.Skip(1).Select(
                    y => new Function.Parameter(y.Name, y.Type.ToConcreteType(pendingBType), y.IsSpread)
                ).ToList();
                var returnType = x.ReturnType.ToConcreteType(pendingBType);

                AllocateFunction(x.Name, parameters, returnType);
            }

            foreach (var x in pendingSuperType.fields)
            {
                var fieldType = x.Type.ToConcreteType(pendingBType);
                AllocateField(x.Name, fieldType, x.Getter != null, x.Setter != null);
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

            FunctionCandidates? candidates;
            if ((candidates = FindFunctionCandidates(name)) != null)
                return candidates;

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
                
                var @this = compiler.BinOpLHS == null ? Operand.This : compiler.BinOpLHS.Operand;

                if (field.Getter != null)
                {
                    var functionStorage = EmitGetForSymbol(field.Getter);
                    var storage = compiler.CurrentFunction.AllocateRegisterStorage(field.Getter.ReturnType);
                    compiler.CurrentFunction.Body.Emit(
                        Opcode.Call,
                        functionStorage.Operand,
                        storage.Operand,
                        new [] { @this }
                    );

                    return storage;
                }
                else
                {
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

            if (symbol is FunctionCandidates candidates)
            {
                return new Storage(
                    new CobType(eCobType.Function, tag: candidates),
                    Operand.None
                );
            }

            return null;
        }

        public bool EmitSetForSymbol(ISymbol symbol)
        {
            if (symbol is Field field)
            {
                // TODO Setter
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

    internal sealed class Indexer
    {
        public CobType KeyType { get; init; }

        public CobType ReturnType { get; init; }

        public Function? Getter { get; init; }

        public Function? Setter { get; init; }
    }

    internal sealed class FunctionCandidates : ISymbol
    {
        private readonly IReadOnlyList<Function> candidates;

        public FunctionCandidates(IReadOnlyList<Function> candidates)
        {
            this.candidates = candidates;
        }

        public Function? ResolveSingle(IReadOnlyList<CobType> arguments)
        {
            return candidates.SingleOrDefault(x =>
            {
                var parameterCount = x.Parameters.Count;
                var parametersCountRequired = x.Parameters.Count(y => y.DefaultValue == null);
                var hasSpreadParameter = parameterCount > 0 && x.Parameters[^1].IsSpread;

                var argumentCount = arguments.Count;
                var paramOffset = 0;

                if (x.CallingConvention == CallingConvention.ThisCall)
                    ++paramOffset;

                if (parameterCount < argumentCount + paramOffset && !hasSpreadParameter)
                    return false;
                if (argumentCount + paramOffset < parametersCountRequired)
                    return false;

                for (int i = 0; i < argumentCount; ++i)
                {
                    var a = arguments[i];
                    CobType b;
                    if (hasSpreadParameter && i >= parameterCount - 1)
                    {
                        // Spread argument
                        b = x.Parameters[parameterCount - 1].Type;
                        b = CobType.Any; // TODO Need a way to get the ElementType from Array intrinsic
                    }
                    else
                        b = x.Parameters[i + paramOffset].Type;

                    if (!CobType.IsCastable(a, b))
                        return false;
                }

                return true;
            });
        }

        public bool IsVisibleTo(IScopeContext context) => candidates.Any(x => x.IsVisibleTo(context));

        public override string ToString() => string.Join('\n', candidates);
    }
}
