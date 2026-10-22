using Compiler.Ast;
using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Ast.Visitors;
using Compiler.CodeGeneration.Artifacts;
using Compiler.Interpreter;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.CodeGeneration
{
    internal sealed class Compiler : IExpressionVisitor<Storage?>
    {
        public List<ArtifactDirective> ArtifactDirectives => artifact.ArtifactDirectives;

        public List<Import> Imports => artifact.Imports;

        public List<Export> Exports => artifact.Exports;

        public List<Module> Modules => artifact.Modules;

        public List<RecordType> RecordTypes => artifact.RecordTypes;

        public List<CobType> Types => artifact.Types;

        public List<Function> Functions => artifact.Functions;

        public List<Variable> Globals => artifact.Globals;

        public List<ScriptExpression> Scripts { get; }

        public Module RootModule { get; }

        public Module Errors { get; }

        public Function FunctionStringError { get; set; } // HACK TODO Cleanup implementation

        public bool IsDivingInitializer { get; private set; }

        public IScopeContext CurrentContext => contextStack.Peek();

        public Module CurrentModule => contextStack.OfType<Module>().First();

        public Function CurrentFunction => contextStack.OfType<Function>().First();

        private Storage? implicitContext;
        private IScopeContext? forTempBlockScope;

        private readonly List<GenericTypeAstReference> genericTypeAstReferences;
        private readonly List<ConcreteTypeAstReference> concreteTypeAstReferences;
        private readonly Stack<HashSet<ISymbol>> mutationStack;
        private readonly Stack<IScopeContext> contextStack;
        private readonly Stack<LoopContext> loopStack;
        private readonly Artifact artifact;
        private readonly MessageCollection messages;

        private Compiler(Artifact artifact, MessageCollection messages)
        {
            this.artifact = artifact;
            this.messages = messages;

            Scripts = new List<ScriptExpression>();
            RootModule = new Module(null, null, this);
            Errors = RootModule.FindOrAllocateModule("error");
            Errors.InitializerFunction.Body.Emit(Opcode.Return);

            genericTypeAstReferences = new List<GenericTypeAstReference>();
            concreteTypeAstReferences = new List<ConcreteTypeAstReference>();
            mutationStack = new Stack<HashSet<ISymbol>>();
            contextStack = new Stack<IScopeContext>();
            loopStack = new Stack<LoopContext>();

            Modules.Add(RootModule);
            contextStack.Push(new RootScopeContext(this));
        }

        public Storage? Visit(ScriptExpression expression)
        {
            contextStack.Push(RootModule);
            contextStack.Push(RootModule.InitializerFunction);

            var expressions = expression.Expressions;
            for (int i = 0; i < expressions.Count; ++i)
                expressions[i].Accept(this);

            //CurrentModule.InitializerFunction.Body.Emit(Opcode.Return);

            contextStack.Pop();
            contextStack.Pop();

            return null;
        }

        public Storage? Visit(ImportStatement expression)
        {
            return null;
        }
        
        public Storage? Visit(ExportStatement expression)
        {
            var function = expression.FunctionExpression.Accept(this);
            var export = new Export { Function = function.Type.TagFunction };
            Exports.Add(export);
            return null;
        }

        public Storage? Visit(ArtifactStatement expression)
        {
            return null;
        }

        public Storage? Visit(ModuleStatement expression)
        {
            var module = CurrentModule.FindModule(expression.Name);

            contextStack.Push(module);
            contextStack.Push(module.InitializerFunction);

            if (expression.Block != null)
            {
                expression.Block.Accept(this)?.Free();

                CurrentFunction.Body.Emit(Opcode.Return);

                contextStack.Pop();
                contextStack.Pop();
            }

            return null;
        }

        public Storage? Visit(TypeDeclStatement expression)
        {
            expression.SuperTypeName.Record?.Accept(this);

            return null;
        }

        public Storage? Visit(EnumRefinementDeclStatement expression)
        {
            return null;
        }

        public Storage? Visit(ErrorStatement expression)
        {
            return null;
        }

        public Storage? Visit(RecordStatement expression)
        {
            var structType = CurrentModule.FindRecordType(expression.Name)!;
            if (structType.IsGeneric)
            {
                genericTypeAstReferences.Add(new GenericTypeAstReference(
                    structType,
                    expression,
                    new Stack<IScopeContext>(contextStack)
                ));
            }
            else
            {
                contextStack.Push(structType);

                foreach (var constant in expression.Constants)
                    constant.Accept(this);

                foreach (var constructExpression in expression.Constructors)
                    constructExpression.Accept(this);

                foreach (var destructExpression in expression.Destructors)
                    destructExpression.Accept(this);

                foreach (var functionExpression in expression.Functions)
                    functionExpression.Accept(this);

                foreach (var fieldExpression in expression.Fields)
                {
                    var field = structType.FindField(fieldExpression.Name);

                    if (field?.Getter != null && fieldExpression.GetterExpression != null)
                    {
                        contextStack.Push(field.Getter);
                        
                        var retStorage = fieldExpression.GetterExpression?.Accept(this);
                        if (retStorage != null)
                        {
                            CurrentFunction.Body.Emit(Opcode.Return, retStorage.Operand);
                            retStorage.Free();
                        }

                        contextStack.Pop();
                    }
                    
                    if (field?.Setter != null && fieldExpression.SetterExpression != null)
                    {
                        contextStack.Push(field.Setter);
                        fieldExpression.SetterExpression?.Accept(this)?.Free();
                        CurrentFunction.Body.Emit(Opcode.Return, Operand.ImmediateUnsigned(0));
                        contextStack.Pop();
                    }
                }

                if (structType.Indexer != null)
                {
                    if (structType.Indexer.Getter != null)
                    {
                        contextStack.Push(structType.Indexer.Getter);
                        
                        var retStorage = expression.Indexer?.GetterExpression?.Accept(this);
                        if (retStorage != null)
                        {
                            CurrentFunction.Body.Emit(Opcode.Return, retStorage.Operand);
                            retStorage.Free();
                        }

                        contextStack.Pop();
                    }

                    if (structType.Indexer.Setter != null)
                    {
                        contextStack.Push(structType.Indexer.Setter);
                        expression.Indexer?.SetterExpression?.Accept(this)?.Free();
                        CurrentFunction.Body.Emit(Opcode.Return, Operand.ImmediateUnsigned(0));
                        contextStack.Pop();
                    }
                }

                contextStack.Pop();
            }

            foreach (var mixin in expression.Mixins)
            {
                var traitType = CobType.FromTypeName(mixin, CurrentContext);
                if (!structType.IsTraitSignatureMatch(traitType.TagRecord))
                    messages.Add(Message.TraitNotFullyImplemented, expression, mixin);
            }

            return null;
        }

        // TODO This is a function, should be merged with FunctionDeclStatement perhaps?
        public Storage? Visit(ConstructStatement expression)
        {
            Function function;
            if (CurrentContext is RecordType recordType)
                function = recordType.FindConstructor(expression.Name)!;
            else
                throw new InvalidOperationException(); // TODO ??

            var mutations = new HashSet<ISymbol>();
            mutationStack.Push(mutations);
            contextStack.Push(function);

            expression.Body?.Accept(this)?.Free();

            function.ReturnLabel.Mark();
            function.Body.Emit(Opcode.Return, Operand.This); // TODO Error if not all paths return

            contextStack.Pop();
            mutationStack.Pop();

            foreach (var x in mutations) function.MutatedSymbols.Add(x);

            if (expression.Name != null && !recordType.IsFullyInitialized(mutations))
                messages.Add(Message.NotAllFieldsInitialized, expression);

            return new Storage(
                new CobType(eCobType.Function, tag: function),
                Operand.None
            );
        }

        // TODO This is a function, should be merged with FunctionDeclStatement perhaps?
        public Storage? Visit(DestructStatement expression)
        {
            Function function;
            if (CurrentContext is RecordType recordType)
                function = recordType.FindDestructor(expression.Name)!;
            else
                throw new InvalidOperationException(); // TODO ??

            var mutations = new HashSet<ISymbol>();
            mutationStack.Push(mutations);
            contextStack.Push(function);

            expression.Body?.Accept(this)?.Free();

            function.ReturnLabel.Mark();
            function.Body.Emit(Opcode.Return, Operand.This); // TODO Error if not all paths return

            contextStack.Pop();
            mutationStack.Pop();

            foreach (var x in mutations) function.MutatedSymbols.Add(x);

            return new Storage(
                new CobType(eCobType.Function, tag: function),
                Operand.None
            );
        }

        public Storage? Visit(FunctionDeclStatement expression)
        {
            // NOTE Null body errors are handled in forward declaration
            if (expression.Body == null)
                return null;

            // TODO Make a clean API for this
            // TODO Also, record types have function candidates
            Function function;
            if (CurrentContext is RecordType recordType)
                function = recordType.FindFunction(expression.Name)!;
            else
            {
                var candidates = CurrentModule.FindFunctionCandidates(expression.Name)!;
                var parameterTypes = expression.Parameters.Select(x => CobType.FromTypeName(x.TypeName, CurrentContext)).ToList();
                function = candidates.ResolveSingle(parameterTypes)!;
                //function = CurrentModule.FindFunction(expression.Name)!;
            }

            if (function == null) throw new InvalidOperationException("Unable to resolve declared function");

            var mutations = new HashSet<ISymbol>();
            mutationStack.Push(mutations);
            contextStack.Push(function);
            forTempBlockScope = null;

            // TODO Temporary hack to call our initializer functions in the entry point
            if (HACK_isDivingFatArrow)
            {
                foreach (var module in Modules)
                {
                    CurrentFunction.Body.Emit(
                        Opcode.Call,
                        Operand.Function(Functions.FindIndex(x => x.FullyQualifiedName == module.InitializerFunction.FullyQualifiedName)),
                        Operand.R0,
                        Array.Empty<Operand>()
                    );
                }
            }

            expression.Body.Accept(this);
            
            function.ReturnLabel.Mark();
            function.Body.Emit(Opcode.Return); // TODO Error if not all paths return

            contextStack.Pop();
            mutationStack.Pop();

            foreach (var x in mutations) function.MutatedSymbols.Add(x);

            return new Storage(
                new CobType(eCobType.Function, tag: function),
                Operand.None
            );
        }

        public Storage? Visit(MixinDeclStatement expression)
        {
            var context = CurrentModule.FindSymbol(expression.TargetTypeName) as IScopeContext;
            contextStack.Push(context);

            expression.Function?.Accept(this);

            if (expression.Field != null)
            {
                Field? field;
                if (context is RecordType recordType)
                    field = recordType.FindField(expression.Field.Name);
                else
                    field = null;

                if (field?.Getter != null && expression.Field.GetterExpression != null)
                {
                    contextStack.Push(field.Getter);

                    var retStorage = expression.Field.GetterExpression?.Accept(this);
                    if (retStorage != null)
                    {
                        CurrentFunction.Body.Emit(Opcode.Return, retStorage.Operand);
                        retStorage.Free();
                    }

                    contextStack.Pop();
                }
                    
                if (field?.Setter != null && expression.Field.SetterExpression != null)
                {
                    contextStack.Push(field.Setter);
                    expression.Field.SetterExpression?.Accept(this)?.Free();
                    CurrentFunction.Body.Emit(Opcode.Return, Operand.ImmediateUnsigned(0));
                    contextStack.Pop();
                }
            }

            contextStack.Pop();

            return null;
        }

        public Storage? Visit(VariableDeclStatement expression)
        {
            // TODO More cleanup required

            var mutable = expression.Type == TokenType.Var;
            for (var i = 0; i < expression.Declarations.Count; ++i)
            {
                var x = expression.Declarations[i];
                if (x is VariableDeclStatement.StandardDeclaration stdDecl)
                {
                    var rhs = stdDecl.Initializer.Accept(this);
                    if (rhs == null || rhs.Type == eCobType.None)
                    {
                        messages.Add(Message.MalformedVarDeclaration, expression);
                        continue;
                    }

                    // TODO global const should not be runtime initialized but rather should be initialized in
                    //      the .rdata section; VM should have an equivalent to this

                    Operand dstOperand;
                    if (CurrentContext == CurrentFunction && CurrentContext is Function
                    &&  !CurrentFunction.Name.StartsWith('$')) // Local Decl
                    {
                        var varType = rhs.Type.ToImpliedType(this);
                        var local = CurrentFunction.AllocateLocal(stdDecl.Name, varType, mutable);
                        var idx = CurrentFunction.FindLocalIndex(local);
                        dstOperand = Operand.Local(idx);
                    }
                    else // Global Decl
                    {
                        dstOperand = null;
                        foreach (var context in contextStack)
                        {
                            var global = context.FindSymbol(stdDecl.Name)!;
                            if (global == null)
                                continue;

                            var globalStorage = context.EmitGetForSymbol(global);
                            dstOperand = globalStorage.Operand;
                            globalStorage.Free();

                            if (global.Type == eCobType.None && global is ILateTypeBinding lateBinding)
                                lateBinding.RebindType(rhs.Type);

                            break;
                        }

                        if (dstOperand == null)
                            throw new InvalidOperationException();
                    }

                    CurrentFunction.Body.Emit(
                        Opcode.Move,
                        dstOperand,
                        rhs.Operand
                    );

                    rhs.Free();
                }
                else if (x is VariableDeclStatement.DestructureDeclaration desDecl)
                {
                    var rhs = desDecl.Initializer.Accept(this);
                    if (rhs == null || rhs.Type == eCobType.None)
                    {
                        messages.Add(Message.MalformedVarDeclaration, expression);
                        continue;
                    }

                    var source = rhs.Type.Tag as IScopeContext;
                    var prevAssignmentRHS = AssignmentRHS;
                    AssignmentRHS = rhs;

                    for (var j = 0; j < desDecl.Fields.Count; ++j)
                    {
                        var fieldName = desDecl.Fields[j];

                        var prevBinOpLHS = BinOpLHS;
                        BinOpLHS = rhs;
                        var srcSymbol = source.FindSymbol($"'{j}");
                        var srcStorage = source.EmitGetForSymbol(srcSymbol);
                        BinOpLHS = prevBinOpLHS;

                        CurrentFunction.AllocateLocal(fieldName, srcStorage.Type, mutable);

                        AssignmentRHS = srcStorage;
                        var dstSymbol = CurrentContext.FindSymbol(fieldName);
                        CurrentContext.EmitSetForSymbol(dstSymbol);
                        srcStorage?.Free();
                    }

                    AssignmentRHS = prevAssignmentRHS;

                    rhs.Free();

                    return null;
                }
            }

            return null;
        }

        public Storage? Visit(IfStatement expression)
        {
            var scope = CurrentFunction.PushBlockScope();

            ++conditionalStack;
            var conditional = expression.Conditional.Accept(this);
            --conditionalStack;

            if (conditional == null || conditional.Type != CobType.Boolean)
            {
                messages.Add(Message.TypeMismatch, expression.Conditional, CobType.Boolean, conditional?.Type);
                return null;
            }
            else if (expression.Conditional is IdentifierExpression)
            {
                var c = CurrentFunction.AllocateRegisterStorage(CobType.Boolean);
                CurrentFunction.Body.Emit(Opcode.CmpEQ, c.Operand, conditional.Operand, Operand.ImmediateUnsigned(1));
                conditional.Free();
                conditional = c;
            }

            var elseLabel = CurrentFunction.Body.AllocateLabel();
            var endLabel = CurrentFunction.Body.AllocateLabel();
            CurrentFunction.Body.Emit(Opcode.JmpF, conditional.Operand, elseLabel);
            conditional.Free();

            CurrentFunction.PushBlockScope();
            if (!scope.CommitNarrowingDirectives(false))
                messages.Add(Message.IllegalNarrowingConstraints, expression.Else);

            expression.Then.Accept(this)?.Free();
            
            scope.RevertNarrowingDirectives();
            CurrentFunction.PopBlockScope();
            
            CurrentFunction.Body.Emit(Opcode.Jmp, endLabel);
            elseLabel.Mark();

            if (expression.Else != null)
            {
                CurrentFunction.PushBlockScope();
                if (!scope.CommitNarrowingDirectives(true))
                    messages.Add(Message.IllegalNarrowingConstraints, expression.Else);
                
                expression.Else.Accept(this)?.Free();
                
                scope.RevertNarrowingDirectives();
                CurrentFunction.PopBlockScope();
            }

            endLabel.Mark();

            CurrentFunction.PopBlockScope();

            return null;
        }

        public Storage? Visit(ForStatement expression)
        {
            // TODO Generator
            // TODO Conditional
            // TODO IsContinue
            // TODO Label

            var startLabel = CurrentFunction.Body.AllocateLabel();
            var endLabel = CurrentFunction.Body.AllocateLabel();

            loopStack.Push(new LoopContext(expression.Tag?.Value, startLabel, endLabel));
            forTempBlockScope = CurrentFunction.PushBlockScope();

            // ENUMERABLE
            var enumerableStorage = expression.Enumerable.Accept(this);

            Storage? getEnumeratorFn;
            if (enumerableStorage != null && enumerableStorage.Type.Tag is IScopeContext enumerableScopeContext)
            {
                var getEnumeratorFnSymbol = enumerableScopeContext.FindSymbol("GetEnumerator");
                getEnumeratorFn = getEnumeratorFnSymbol != null
                                ? enumerableScopeContext.EmitGetForSymbol(getEnumeratorFnSymbol)
                                : null;
                if (getEnumeratorFn?.Type.TagFunctionCandidates != null)
                {
                    getEnumeratorFn = enumerableScopeContext.EmitGetForSymbol(
                        getEnumeratorFn.Type.TagFunctionCandidates.ResolveSingle(null)
                    );
                }
            }
            else
                getEnumeratorFn = null;

            if (getEnumeratorFn == null || getEnumeratorFn.Type.TagFunction == null)
            {
                messages.Add(Message.CannotEnumerateType, expression.Enumerable, enumerableStorage?.Type);
                return null;
            }

            var enumeratorType = getEnumeratorFn.Type.TagFunction.ReturnType;
            if (enumeratorType.Tag is RecordType recordType)
            {
                var superType = recordType.HACK_PendingSuperType;
                if (recordType.PopulateConcretizedRecordIfRequired())
                    concreteTypeAstReferences.Add(new ConcreteTypeAstReference(superType, recordType));
            }

            // FiniteEnumerator`TKey`TValue^
            // InfiniteEnumerator`TValue^
            var tvalueType = CobType.Int; // TODO Correctly resolve this from In/FiniteEnumerator
            var enumeratorStorage = CurrentFunction.AllocateRegisterStorage(enumeratorType);

            // $tmp = GetEnumerator()
            CurrentFunction.Body.Emit(
                Opcode.Call,
                getEnumeratorFn.Operand,
                enumeratorStorage.Operand,
                new[] { enumerableStorage!.Operand }
            );
            getEnumeratorFn.Free();
            enumerableStorage.Free();

            startLabel.Mark();

            // if (!$tmp.MoveNext()) break;

            Storage? moveNextFn;
            if (enumeratorStorage.Type.Tag is IScopeContext enumeratorScopeContext)
            {
                var moveNextFnSymbol = enumeratorScopeContext.FindSymbol("MoveNext");
                moveNextFn = moveNextFnSymbol != null
                           ? enumeratorScopeContext.EmitGetForSymbol(moveNextFnSymbol)
                           : null;
                if (moveNextFn?.Type.TagFunctionCandidates != null)
                {
                    moveNextFn = enumeratorScopeContext.EmitGetForSymbol(
                        moveNextFn.Type.TagFunctionCandidates.ResolveSingle(null)
                    );
                }
            }
            else
            {
                moveNextFn = null;
            }

            if (moveNextFn == null || moveNextFn.Type.TagFunction == null)
            {
                messages.Add(Message.CannotEnumerateType, expression.Enumerable, enumerableStorage?.Type);
                return null;
            }

            var moveNextResultStorage = CurrentFunction.AllocateRegisterStorage(moveNextFn.Type.TagFunction.ReturnType);
            CurrentFunction.Body.Emit(
                Opcode.Call,
                moveNextFn.Operand,
                moveNextResultStorage.Operand,
                new[] { enumeratorStorage.Operand }
            );

            CurrentFunction.Body.Emit(Opcode.JmpF, moveNextResultStorage.Operand, endLabel);
            moveNextResultStorage.Free();

            var localValue = CurrentFunction.AllocateLocal(expression.ValueIdentifier, tvalueType, true);
            var localValueIdx = CurrentFunction.FindLocalIndex(localValue);
            var localValueOperand = Operand.Local(localValueIdx);

            CurrentFunction.Body.Emit(
                Opcode.GetField,
                localValueOperand,
                enumeratorStorage.Operand,
                Operand.ImmediateUnsigned(0)
            );

            //index.Free();

            expression.Body.Accept(this)?.Free();

            CurrentFunction.Body.Emit(Opcode.Jmp, startLabel);
            endLabel.Mark();

            CurrentFunction.PopBlockScope();
            loopStack.Pop();

            return null;
        }

        public Storage? Visit(WhileStatement expression)
        {
            var startLabel = CurrentFunction.Body.AllocateLabel();
            var endLabel = CurrentFunction.Body.AllocateLabel();

            loopStack.Push(new LoopContext(expression.Tag?.Value, startLabel, endLabel));
            CurrentFunction.PushBlockScope();

            // INITIALIZER
            expression.Initializer?.Accept(this)?.Free();

            // CONDITIONAL
            startLabel.Mark();
            if (expression.Conditional != null)
            {
                var conditional = expression.Conditional.Accept(this);
                // TODO Error if conditional is null

                CurrentFunction.Body.Emit(Opcode.JmpF, conditional.Operand, endLabel);

                conditional.Free();
            }

            // BODY
            expression.Body.Accept(this)?.Free();

            CurrentFunction.Body.Emit(Opcode.Jmp, startLabel);
            endLabel.Mark();

            CurrentFunction.PopBlockScope();
            loopStack.Pop();

            return null;
        }

        public Storage? Visit(ContinueStatement expression)
        {
            if (loopStack.Count == 0)
            {
                messages.Add(Message.CannotContinue, expression);
                return null;
            }

            CurrentFunction.Body.Emit(Opcode.Jmp, loopStack.Peek().Continue);

            return null;
        }

        public Storage? Visit(BreakStatement expression)
        {
            if (loopStack.Count == 0)
            {
                messages.Add(Message.CannotBreak, expression);
                return null;
            }

            Label label;
            if (expression.Label == null)
                label = loopStack.Peek().Break;
            else
            {
                var context = loopStack.FirstOrDefault(x => x.Tag == expression.Label.Value);
                if (context == null)
                {
                    messages.Add(Message.CannotBreakNoLabel, expression, expression.Label.Value);
                    return null;
                }

                label = context.Break;
            }

            CurrentFunction.Body.Emit(Opcode.Jmp, label);

            return null;
        }

        public Storage? Visit(ReturnStatement expression)
        {
            var rhs = expression.Expression?.Accept(this);

            if (rhs == null)
                CurrentFunction.Body.Emit(Opcode.Return);
            else
            {
                if (CurrentFunction.ReturnType == eCobType.None)
                    CurrentFunction.ReturnType = rhs.Type;
                else if (!CobType.IsExplicitCastable(CurrentFunction.ReturnType, rhs.Type, rhs.Operand.Value))
                    messages.Add(Message.ReturnTypeMismatch, expression, CurrentFunction.ReturnType, rhs.Type);

                // TODO Might clobber rhs storage...
                EnforceRefinment(rhs, CurrentFunction.ReturnType, expression); // TODO Should be implemented in IsExplicitCastable?

                CurrentFunction.Body.Emit(Opcode.Return, rhs.Operand);
                rhs.Free();
            }

            return null;
        }

        public Storage? Visit(MachineStatement expression)
        {
            if (expression.Target == "cobil")
            {
                if (expression.Source == "Lens_Get")
                {
                    CurrentFunction.Body.Emit(Opcode.GetField, Operand._Register(1), Operand.This, Operand.ImmediateUnsigned(0));
                    CurrentFunction.Body.Emit(Opcode.Add, Operand._Register(1), Operand._Register(1), Operand.Argument(1));
                    CurrentFunction.Body.Emit(Opcode.Peek, Operand._Register(0), Operand._Register(1), Operand.ImmediateUnsigned(1));
                    CurrentFunction.Body.Emit(Opcode.Return, Operand._Register(0));
                }
                else if (expression.Source == "Lens_Set")
                {
                    CurrentFunction.Body.Emit(Opcode.GetField, Operand._Register(1), Operand.This, Operand.ImmediateUnsigned(0));
                    CurrentFunction.Body.Emit(Opcode.Add, Operand._Register(1), Operand._Register(1), Operand.Argument(1));
                    CurrentFunction.Body.Emit(Opcode.Poke, Operand._Register(1), Operand.ImmediateUnsigned(1), Operand.Argument(2));
                    CurrentFunction.Body.Emit(Opcode.Return, Operand.ImmediateUnsigned(0));
                }
            }

            return null;
        }

        public Storage? Visit(BlockExpression expression)
        {
            CurrentFunction.PushBlockScope();

            for (var i = 0; i < expression.Expressions.Count; i++)
            {
                var expr = expression.Expressions[i];

                implicitContext = null;

                var retStorage = expr.Accept(this);
                if (expr is FatArrowStatement)
                {
                    CurrentFunction.PopBlockScope();
                    return retStorage;
                }

                retStorage?.Free();
            }

            CurrentFunction.PopBlockScope();

            return null;
        }

        private bool HACK_isDivingFatArrow;

        public Storage? Visit(FatArrowStatement expression)
        {
            HACK_isDivingFatArrow = true;

            var value = expression.Expression.Accept(this);
            if (CurrentModule.IsRoot && value != null && value.Type == eCobType.Function)
            {
                if (artifact.EntryFunction != null)
                    messages.Add(Message.CannotRedeclareEntryPoint, expression);
                else
                {
                    artifact.EntryFunction = value.Type.TagFunction;
                }
            }

            HACK_isDivingFatArrow = false;

            return value;
        }


        public Storage? BinOpLHS { get; set; } // TODO Probably a hack tbh
        public Storage? AssignmentRHS { get; set; }
        private int conditionalStack;
        private bool isUnionAssignment;

        public Storage? Visit(BinaryOperatorExpression expression)
        {
            // Assignment
            var asnOpcode = expression.Operator switch
            {
                TokenType.Assign => Opcode.Move,
                TokenType.UnionAssign => Opcode.Move,
                TokenType.AddAssign => Opcode.Add,
                TokenType.SubtractAssign => Opcode.Sub,
                TokenType.MultiplyAssign => Opcode.Mul,
                TokenType.DivideAssign => Opcode.Div,
                TokenType.DivideCeilAssign => Opcode.DivCeil,
                TokenType.DivideFloorAssign => Opcode.DivFloor,
                TokenType.RemainderAssign => Opcode.Rem,
                TokenType.ModuloAssign => Opcode.Mod,
                TokenType.BitLeftShiftAssign => Opcode.BitShl,
                TokenType.BitRightShiftAssign => Opcode.BitShr,
                TokenType.BitLeftRotateAssign => Opcode.BitRol,
                TokenType.BitRightRotateAssign => Opcode.BitRor,
                TokenType.BitAndAssign => Opcode.BitAnd,
                TokenType.BitOrAssign => Opcode.BitOr,
                TokenType.BitXorAssign => Opcode.BitXor,
                _ => Opcode.None
            };

            // Equality
            var cmpOpcode = expression.Operator switch
            {
                TokenType.Equals => Opcode.CmpEQ,
                TokenType.NotEquals => Opcode.CmpNEQ,
                TokenType.LessThan => Opcode.CmpLT,
                TokenType.LessThanOrEquals => Opcode.CmpLTE,
                TokenType.MoreThan => Opcode.CmpGT,
                TokenType.MoreThanOrEquals => Opcode.CmpGTE,
                _ => Opcode.None
            };

            // Conditionals
            var cndOpcode = expression.Operator switch
            {
                TokenType.ConditionalAnd => Opcode.CondAnd,
                TokenType.ConditionalOr => Opcode.CondOr,
                _ => Opcode.None
            };

            // Arithmetic
            var artOpcode = expression.Operator switch
            {
                TokenType.Add => Opcode.Add,
                TokenType.Subtract => Opcode.Sub,
                TokenType.Multiply => Opcode.Mul,
                TokenType.Exponent => Opcode.Pow,
                TokenType.Divide => Opcode.Div,
                TokenType.DivideCeil => Opcode.DivCeil,
                TokenType.DivideFloor => Opcode.DivFloor,
                TokenType.Remainder => Opcode.Rem,
                TokenType.Modulo => Opcode.Mod,
                TokenType.BitLeftShift => Opcode.BitShl,
                TokenType.BitRightShift => Opcode.BitShr,
                TokenType.BitLeftRotate => Opcode.BitRol,
                TokenType.BitRightRotate => Opcode.BitRor,
                TokenType.BitAnd => Opcode.BitAnd,
                TokenType.BitOr => Opcode.BitOr,
                TokenType.BitXor => Opcode.BitXor,
                _ => Opcode.None
            };

            if (expression.Operator == TokenType.Generic)
            {
                var lhs = expression.Left.Accept(this);

                if (expression.Right is not IdentifierExpression rhs)
                {
                    messages.Add(Message.UnexpectedToken2, expression.Right, "type name", expression.Right.Token);
                    return null;
                }

                // TODO Support T in function bodies to remove the hack below
                var aType = lhs?.Type;
                //var bType = CobType.FromString(rhs.Value);
                var bType = rhs.Value == "T" // HACK TODO THIS IS ENTIRELY INCORRECT
                    ? CobType.U8
                    : CobType.FromTypeName(new TypeName { Type = eTypeName.Identifier, Identifier = rhs.Value }, CurrentContext);
                CobType cType;

                if (aType?.Tag is RecordType recordType)
                {
                    var tag = recordType.FindConcretizedRecord(bType) ?? recordType.AllocateConcretizedRecord(bType);

                    cType = new CobType(eCobType.Record, tag: tag);

                    if (tag.PopulateConcretizedRecordIfRequired())
                        concreteTypeAstReferences.Add(new ConcreteTypeAstReference(recordType, tag));
                }
                else
                {
                    messages.Add(Message.IllegalTypeName, expression);
                    return null;
                }

                lhs?.Free();

                return new Storage(cType, Operand.None);
            }
            else if (expression.Operator == TokenType.NilCoalesce) // ??
            {
                var lhs = expression.Left.Accept(this);
                var rhs = expression.Right.Accept(this);

                if (!lhs.Type.HasNilFlag)
                {
                    messages.Add(Message.CannotNilCoalesceType, expression, lhs.Type);
                    return null;
                }
                // TODO Validate RHS type matches LHS

                var c = CurrentFunction.AllocateRegisterStorage(CobType.Boolean);
                
                var labelEnd = CurrentFunction.Body.AllocateLabel();
                CurrentFunction.Body.Emit(Opcode.CmpTyEQ, c.Operand, lhs.Operand, CobType.Nil.ToOperand(artifact));
                CurrentFunction.Body.Emit(Opcode.JmpF, c.Operand, labelEnd);
                CurrentFunction.Body.Emit(Opcode.Move, lhs.Operand, rhs.Operand);
                labelEnd.Mark();

                c.Free();
                rhs?.Free();

                return lhs;
            }
            else if (expression.Operator == TokenType.ErrorCoalesce) // !!
            {
                ++conditionalStack;
                var lhs = expression.Left.Accept(this);
                var rhs = expression.Right.Accept(this);
                --conditionalStack;

                if (!lhs.Type.HasErrorFlag)
                {
                    messages.Add(Message.CannotErrorCoalesceType, expression, lhs.Type);
                    return null;
                }
                // TODO Validate RHS type matches LHS

                var c = CurrentFunction.AllocateRegisterStorage(CobType.Boolean);

                var labelEnd = CurrentFunction.Body.AllocateLabel();
                CurrentFunction.Body.Emit(Opcode.CmpTyEQ, c.Operand, lhs.Operand, CobType.Error.ToOperand(artifact));
                CurrentFunction.Body.Emit(Opcode.JmpF, c.Operand, labelEnd);
                CurrentFunction.Body.Emit(Opcode.Move, lhs.Operand, rhs.Operand);
                labelEnd.Mark();

                c.Free();
                rhs?.Free();

                return lhs;
            }
            else if (expression.Operator == TokenType.NilErrorCoalesce) // ?!
            {
                ++conditionalStack;
                var lhs = expression.Left.Accept(this);
                var rhs = expression.Right.Accept(this);
                --conditionalStack;

                if (!lhs.Type.HasErrorFlag)
                {
                    messages.Add(Message.CannotErrorCoalesceType, expression, lhs.Type);
                    return null;
                }
                if (!lhs.Type.HasNilFlag)
                {
                    messages.Add(Message.CannotNilCoalesceType, expression, lhs.Type);
                    return null;
                }
                // TODO Validate RHS type matches LHS

                var c = CurrentFunction.AllocateRegisterStorage(CobType.Boolean);

                var labelEnd = CurrentFunction.Body.AllocateLabel();
                var labelHandler = CurrentFunction.Body.AllocateLabel();
                CurrentFunction.Body.Emit(Opcode.CmpTyEQ, c.Operand, lhs.Operand, CobType.Nil.ToOperand(artifact));
                CurrentFunction.Body.Emit(Opcode.JmpT, c.Operand, labelHandler);
                CurrentFunction.Body.Emit(Opcode.CmpTyEQ, c.Operand, lhs.Operand, CobType.Error.ToOperand(artifact));
                CurrentFunction.Body.Emit(Opcode.JmpT, c.Operand, labelHandler);
                CurrentFunction.Body.Emit(Opcode.Jmp, labelEnd);
                labelHandler.Mark();
                CurrentFunction.Body.Emit(Opcode.Move, lhs.Operand, rhs.Operand);
                labelEnd.Mark();

                c.Free();
                rhs?.Free();

                return lhs;
            }
            else if (expression.Operator == TokenType.Dot) // Dereference a.b
            {
                // TODO Clean this up
                var prevAsnSrc = AssignmentRHS;
                AssignmentRHS = null;
                var lhs = expression.Left.Accept(this);
                AssignmentRHS = prevAsnSrc;
                var prevBinOpLHS = BinOpLHS;
                BinOpLHS = lhs;

                if (lhs?.Type.Tag is not IScopeContext context)
                {
                    messages.Add(Message.CannotDereferenceContext, expression, lhs?.Type);
                    return null;
                }

                if (context is RecordType recordType)
                {
                    if (recordType.PopulateConcretizedRecordIfRequired())
                        concreteTypeAstReferences.Add(new ConcreteTypeAstReference(recordType.HACK_PendingSuperType, recordType));
                }

                implicitContext = lhs;//expression.Left;
                contextStack.Push(context);

                var rhs = expression.Right.Accept(this);
                
                contextStack.Pop();
                lhs?.Free();

                BinOpLHS = prevBinOpLHS;
                return rhs;
            }
            else if (expression.Operator == TokenType.BitPack) // BitPack <<|
            {
                var a = expression.Left.Accept(this);
                var b = expression.Right.Accept(this);

                var cType = CobType.ResolveOperatorResultType(expression.Operator, a?.Type, b?.Type);
                if (cType == CobType.None)
                {
                    messages.Add(Message.IllegalOperationBetweenTypes, expression, expression.Operator.GetDescription(), a?.Type, b?.Type);
                    return null;
                }

                var c = CurrentFunction.AllocateRegisterStorage(cType);
                var shlValue = b.Type.Size;

                CurrentFunction.Body.Emit(Opcode.BitShl, c.Operand, a.Operand, Operand.ImmediateUnsigned(shlValue));
                CurrentFunction.Body.Emit(Opcode.BitOr, c.Operand, a.Operand, b.Operand);
                
                b.Free();
                a.Free();

                return c;
            }
            else if (expression.Operator is TokenType.Range or TokenType.RangeInclusive
                                         or TokenType.RangeLength or TokenType.RangeTerminal)
            {
                var a = expression.Left.Accept(this);
                var b = expression.Right.Accept(this);
                var aType = a?.Type ?? b?.Type ?? CobType.UInt;
                var bType = b?.Type ?? a?.Type ?? CobType.UInt;

                var cType = CobType.ResolveOperatorResultType(expression.Operator, aType, bType);
                if (cType == CobType.None)
                {
                    messages.Add(Message.IllegalOperationBetweenTypes, expression, expression.Operator.GetDescription(), aType, bType);
                    return null;
                }

                var c = CurrentFunction.AllocateRegisterStorage(cType);
                CurrentFunction.Body.Emit(Opcode.New, c.Operand, cType.ToOperand(artifact));

                if (expression.Left is EmptyExpression)
                    a = new Storage(bType, Operand.ImmediateUnsigned(0));
                if (expression.Right is EmptyExpression)
                    b = new Storage(aType, Operand.ImmediateUnsigned(~0));

                if (expression.Operator == TokenType.Range)
                {
                    var d = CurrentFunction.AllocateRegisterStorage(bType);
                    CurrentFunction.Body.Emit(Opcode.Sub, d.Operand, b.Operand, Operand.ImmediateUnsigned(1));
                    b.Free();
                    b = d;
                }
                else if (expression.Operator == TokenType.RangeLength)
                {
                    var d = CurrentFunction.AllocateRegisterStorage(bType);
                    CurrentFunction.Body.Emit(Opcode.Add, d.Operand, a.Operand, b.Operand);
                    b.Free();
                    b = d;
                }
                else if (expression.Operator == TokenType.RangeTerminal)
                {
                    var d = CurrentFunction.AllocateRegisterStorage(bType);
                    CurrentFunction.Body.Emit(Opcode.BitNot, d.Operand, b.Operand);
                    b.Free();
                    b = d;
                }

                // TODO type.FindFieldIndex("Start"), etc.
                CurrentFunction.Body.Emit(Opcode.SetField, c.Operand, Operand.ImmediateUnsigned(0), a.Operand);
                CurrentFunction.Body.Emit(Opcode.SetField, c.Operand, Operand.ImmediateUnsigned(1), b.Operand);

                b.Free();
                a.Free();

                return c;
            }
            else if (cndOpcode != Opcode.None) // Conditional && ||
            {
                var a = expression.Left.Accept(this);
                var b = expression.Right.Accept(this);

                var cType = CobType.ResolveOperatorResultType(expression.Operator, a?.Type, b?.Type);
                if (cType == CobType.None)
                {
                    messages.Add(Message.IllegalOperationBetweenTypes, expression, expression.Operator.GetDescription(), a?.Type, b?.Type);
                    return null;
                }

                var c = CurrentFunction.AllocateRegisterStorage(cType);

                CurrentFunction.Body.Emit(cndOpcode, c.Operand, a.Operand, b.Operand);

                b.Free();
                a.Free();

                return c;
            }
            else if (cmpOpcode != Opcode.None) // Equality == != < > <= >=
            {
                var a = expression.Left.Accept(this);
                var b = expression.Right.Accept(this);

                var cType = CobType.ResolveOperatorResultType(expression.Operator, a?.Type, b?.Type);
                if (cType == CobType.None)
                {
                    messages.Add(Message.IllegalOperationBetweenTypes, expression, expression.Operator.GetDescription(), a?.Type, b?.Type);
                    return null;
                }

                var c = CurrentFunction.AllocateRegisterStorage(cType);

                CurrentFunction.Body.Emit(cmpOpcode, c.Operand, a.Operand, b.Operand);

                b.Free();
                a.Free();

                return c;
            }
            else if (artOpcode != Opcode.None) // Arithmetic + - * /
            {
                var a = expression.Left.Accept(this);
                var b = expression.Right.Accept(this);

                var cType = CobType.ResolveOperatorResultType(expression.Operator, a?.Type, b?.Type);
                if (cType == CobType.None)
                {
                    messages.Add(Message.IllegalOperationBetweenTypes, expression, expression.Operator.GetDescription(), a?.Type, b?.Type);
                    return null;
                }

                var c = CurrentFunction.AllocateRegisterStorage(cType);
                
                CurrentFunction.Body.Emit(artOpcode, c.Operand, a.Operand, b.Operand);
                
                b.Free();
                a.Free();

                return c;
            }
            else if (asnOpcode != Opcode.None) // Assignment = += -=
            {
                Storage? c;
                var b = expression.Right.Accept(this);

                if (asnOpcode != Opcode.Move)
                {
                    // TODO Can be rewritten
                    c = expression.Left.Accept(this);
                    CurrentFunction.Body.Emit(asnOpcode, c.Operand, c.Operand, b.Operand);
                    AssignmentRHS = c;

                    var cType = CobType.ResolveOperatorResultType(expression.Operator, c.Type, b.Type);
                    if (cType == CobType.None)
                    {
                        messages.Add(Message.IllegalOperationBetweenTypes, expression, expression.Operator.GetDescription(), c.Type, b.Type);
                        //return null;
                    }
                }
                else
                {
                    AssignmentRHS = b;
                    c = null;
                }

                isUnionAssignment = expression.Operator == TokenType.UnionAssign;

                var a = expression.Left.Accept(this);

                a?.Free();
                c?.Free();
                b?.Free();

                AssignmentRHS = null;

                return null;
            }
            else
                throw new ArgumentOutOfRangeException(nameof(expression));
        }

        public Storage? Visit(PrefixOperatorExpression expression)
        {
            // TODO Support rhs logical !
            if (expression.Operator == TokenType.Not && conditionalStack != 0) // Logical !
            {
                var b = expression.Right.Accept(this);
                var c = CurrentFunction.AllocateRegisterStorage(b.Type);

                CurrentFunction.Body.Emit(Opcode.Not, c.Operand, b.Operand);

                b.Free();

                return c;
            }
            else if (expression.Operator == TokenType.Spread)
            {
                // This is an error, cannot spread outside of array literals
                // TODO message.Add()
            }
            else if (expression.Operator == TokenType.Dot)
            {
                if (implicitContext == null)
                {
                    messages.Add(Message.CannotDereferenceContext, expression, (string?)null);
                    return null;
                }

                var prevBinOpLHS = BinOpLHS;
                var lhs = implicitContext;
                BinOpLHS = lhs;

                contextStack.Push((IScopeContext)lhs!.Type.Tag!);
                var b = expression.Right.Accept(this);
                contextStack.Pop();

                lhs.Free();
                BinOpLHS = prevBinOpLHS;

                return b;
            }
            else
            {
                var b = expression.Right.Accept(this);
                var c = CurrentFunction.AllocateRegisterStorage(b.Type);

                var opcode = expression.Operator switch
                {
                    TokenType.BitNot => Opcode.BitNot, // TODO Return to using ! instead of ~?
                    TokenType.Subtract => Opcode.Neg,
                    _ => throw new ArgumentOutOfRangeException(nameof(expression))
                };

                CurrentFunction.Body.Emit(opcode, c.Operand, b.Operand);

                b.Free();

                return c;
            }

            return null;
        }

        public Storage? Visit(PostfixOperatorExpression expression)
        {
            if (expression.Operator == TokenType.Not)
            {
                ++conditionalStack;
                var lhs = expression.Left.Accept(this);
                --conditionalStack;

                if (!lhs.Type.HasErrorFlag)
                {
                    messages.Add(Message.CannotErrorCoalesceType, expression, lhs.Type);
                    return null;
                }

                var c = CurrentFunction.AllocateRegisterStorage(CobType.Boolean);

                var labelEnd = CurrentFunction.Body.AllocateLabel();
                CurrentFunction.Body.Emit(Opcode.CmpTyEQ, c.Operand, lhs.Operand, CobType.Error.ToOperand(artifact));
                CurrentFunction.Body.Emit(Opcode.JmpF, c.Operand, labelEnd);
                CurrentFunction.Body.Emit(Opcode.Return, lhs.Operand);
                labelEnd.Mark();

                c.Free();

                return lhs;
            }
            else
                throw new ArgumentOutOfRangeException(nameof(expression));
        }

        public Storage? Visit(PatternMatchExpression expression)
        {
            ++conditionalStack;
            var lhs = expression.Left.Accept(this);
            --conditionalStack;

            if (expression.RightSingle != null)
            {
                var type = CobType.FromTypeName(expression.RightSingle.TypeName, CurrentContext);

                var c = CurrentFunction.AllocateRegisterStorage(CobType.Boolean);

                CurrentFunction.Body.Emit(
                    expression.Operation == TokenType.Is ? Opcode.CmpTyEQ : Opcode.CmpTyNEQ,
                    c.Operand,
                    lhs.Operand,
                    type.ToOperand(artifact)
                );

                // Narrowing
                // TODO Nested narrowing scopes might not actually work as expected, keep track in Compiler instead?
                if (conditionalStack > 0)
                    CurrentFunction.AddNarrowingDirective(lhs.Operand, type);

                lhs.Free();

                return c;
            }
            else if (expression.RightMulti != null)
            {
                if (expression.Operation != TokenType.Is)
                {
                    messages.Add(Message.CannotMatchNotPattern, expression);
                    return null;
                }

                // TODO Validate all types handled

                var seenTypes = new HashSet<CobType>();
                PatternMatchExpression.Pattern? defaultBranch = null;
                CobType? resultType = null;

                var c = CurrentFunction.AllocateRegisterStorage(CobType.Any);
                var labelEnd = CurrentFunction.Body.AllocateLabel();

                foreach (var branch in expression.RightMulti)
                {
                    var labelCaseEnd = CurrentFunction.Body.AllocateLabel();

                    var isDefaultBranch = branch.TypeName?.Identifier == "default";
                    var useDynamicTypeCheck = lhs.Type.Type is eCobType.Union or eCobType.Any;

                    if (isDefaultBranch)
                    {
                        defaultBranch = branch;
                        continue;
                    }

                    // TYPE CHECK
                    CobType type;
                    if (branch.ValueExpression is NumberLiteralExpression or BooleanLiteralExpression
                                               or StringLiteralExpression or NilLiteralExpression)
                    {
                        var value = branch.ValueExpression.Accept(this)!;
                        value.Free();

                        type = value.Type;
                    }
                    else if (branch.TypeName != null)
                        type = CobType.FromTypeName(branch.TypeName, CurrentContext);
                    else
                    {
                        messages.Add(Message.IllegalPattern, branch.Token);
                        continue;
                    }

                    if (useDynamicTypeCheck)
                    {
                        // Dynamic Check
                        var d = CurrentFunction.AllocateRegisterStorage(CobType.Boolean);
                        CurrentFunction.Body.Emit(Opcode.CmpTyEQ, d.Operand, lhs.Operand, type.ToOperand(artifact));
                        CurrentFunction.Body.Emit(Opcode.JmpF, d.Operand, labelCaseEnd);
                        d.Free();
                    }
                    else
                    {
                        // Static Check
                        if (type != lhs.Type)
                        {
                            messages.Add(Message.TypeMismatch, branch.Token, lhs.Type, type);
                            continue;
                        }
                    }

                    seenTypes.Add(type);
                    
                    // VALUE CHECK
                    if (branch.ValueExpression is IdentifierExpression ie)
                    {
                        var local = CurrentFunction.AllocateLocal(ie.Value, type, false);
                        CurrentFunction.Body.Emit(
                            Opcode.Move,
                            Operand.Local(CurrentFunction.FindLocalIndex(local)),
                            lhs.Operand
                        );
                    }
                    else if (branch.ValueExpression is NumberLiteralExpression or BooleanLiteralExpression
                                                    or StringLiteralExpression or NilLiteralExpression)
                    {
                        var value = branch.ValueExpression.Accept(this);
                        var e = CurrentFunction.AllocateRegisterStorage(CobType.Boolean);
                        CurrentFunction.Body.Emit(Opcode.CmpEQ, e.Operand, lhs.Operand, value.Operand);
                        CurrentFunction.Body.Emit(Opcode.JmpF, e.Operand, labelCaseEnd);
                        e.Free();
                        value.Free();
                    }
                    else if (branch.ValueExpression != null)
                        throw new NotImplementedException();

                    // VALUE
                    var rhs = branch.Right?.Accept(this);

                    CurrentFunction.Body.Emit(Opcode.Move, c.Operand, rhs.Operand);
                    CurrentFunction.Body.Emit(Opcode.Jmp, labelEnd);
                    labelCaseEnd.Mark();

                    if (resultType == null || CobType.IsImplicitCastable(resultType, rhs.Type))
                        resultType = rhs.Type;
                    else
                        messages.Add(Message.TypeMismatch, branch.Right, resultType, rhs.Type);

                    rhs?.Free();
                }

                if (defaultBranch != null)
                {
                    var rhs = defaultBranch.Right.Accept(this);
                    
                    CurrentFunction.Body.Emit(Opcode.Move, c.Operand, rhs.Operand);

                    if (resultType == null || CobType.IsImplicitCastable(resultType, rhs.Type))
                        resultType = rhs.Type;
                    else
                        messages.Add(Message.TypeMismatch, defaultBranch.Right, resultType, rhs.Type);
                    
                    rhs?.Free();
                }

                labelEnd.Mark();

                lhs.Free();

                // Validate Types
                if (resultType == null)
                {
                    messages.Add(Message.MissingPattern, expression, "any");
                    return null;
                }

                if (defaultBranch == null)
                {
                    foreach (var expectedType in lhs.Type.YieldTypesInUnion())
                    {
                        if (seenTypes.Contains(expectedType))
                            continue;

                        messages.Add(Message.MissingPattern, expression, expectedType);
                        return null;
                    }
                }

                return new Storage(resultType, c.Operand, CurrentFunction);
            }
            else
                throw new ArgumentOutOfRangeException(nameof(expression));
        }

        public Storage? Visit(CallExpression expression)
        {
            // EXPLICIT CAST
            var storage = VisitExplicitCastExpression(expression);
            if (storage != null)
                return storage;

            var candidatesStorage = expression.FunctionExpression.Accept(this); // TODO Cleanup, merge this with other implementations

            // TUPLE ALLOCATION
            if (storage == null)
                storage = VisitTupleLiteralExpression(expression, candidatesStorage);

            // CALL
            if (storage == null)
                storage = VisitCallExpression(expression, candidatesStorage);

            candidatesStorage?.Free();

            return storage;
        }

        private Storage? VisitExplicitCastExpression(CallExpression expression)
        {
            var dstStorage = expression.FunctionExpression.Accept(this);
            if (dstStorage == null || dstStorage.Operand.Type != OperandType.None) // TODO Would be better to check against Type not None Operand
                return null;
            
            var arguments = new List<Storage>();
            foreach (var argExpression in expression.Arguments)
            {
                var argStorage = argExpression.Accept(this);
                if (argStorage == null)
                    continue;

                arguments.Add(argStorage);
            }

            var function = dstStorage.Type.Tag switch
            {
                FunctionCandidates x => x.ResolveSingle(arguments.Select(y => y.Type).ToList()),
                Function x => x,
                _ => null
            };

            // NOTE The outer call expression handles function calls and tuple ctors
            if (function != null || dstStorage.Type.TagRecord != null)// && !function.IsCasting)
            {
                arguments.ForEach(x => x.Free());
                dstStorage.Free();
                return null; // TODO Handle the call here?
            }

            var source = arguments.FirstOrDefault();
            if (source == null)
            {
                messages.Add(Message.FunctionParameterCountMismatch, expression, 1, arguments.Count);
                return source;
            }

            var dstType = dstStorage.Type; // dstType :: int | string | function() This | ...
            var srcType = source.Type;

            // TODO HACK Resolve int() when no user-defined cast function is found...
            if ((dstType.TagFunction != null || dstType.TagFunctionCandidates != null) && function == null)
                dstType = CobType.FromTypeName(new TypeName { Type = eTypeName.Identifier, Identifier = "int" }, CurrentContext);
            
            if (!CobType.IsExplicitCastable(dstType, srcType))
            {
                messages.Add(Message.CannotCastType, expression, srcType, dstType);
                return source; // NOTE Non-null return prevents superfluous errors from being raised
            }

            var target = EmitCast(source, dstType, expression);
            return target;


            // OLD

            //var arguments = expression.Arguments;
            //if (arguments.Count != 1)
            //{
            //    messages.Add(Message.FunctionParameterCountMismatch, expression, 1, arguments.Count);
            //    return null;
            //}

            //var source = arguments[0].Accept(this);
            //var srcType = source.Type;

            //if (!CobType.IsExplicitCastable(dstType, srcType))
            //{
            //    messages.Add(Message.CannotCastType, expression, srcType, dstType);
            //    return source; // NOTE Non-null return prevents superfluous errors from being raised
            //}

            //var target = EmitCast(source, dstType);

            //return target;
        }

        private Storage? VisitTupleLiteralExpression(CallExpression expression, Storage? functionStorage)
        {
            if (functionStorage == null
            ||  functionStorage.Type.Tag is not RecordType tupleType
            ||  tupleType.Type != eRecordType.Tuple)
                return null;

            functionStorage.Free();

            var cobType = functionStorage.Type;//new CobType(eCobType.Tuple, tag: tupleType);

            var storage = CurrentFunction.AllocateRegisterStorage(cobType);
            CurrentFunction.Body.Emit(Opcode.New, storage.Operand, cobType.ToOperand(artifact));

            for (var i = 0; i < expression.Arguments.Count; ++i)
            {
                var argStorage = expression.Arguments[i].Accept(this);
                CurrentFunction.Body.Emit(
                    Opcode.SetField,
                    storage.Operand,
                    Operand.ImmediateUnsigned(i),
                    argStorage.Operand
                );
            }

            return storage;
        }

        private Storage? VisitCallExpression(CallExpression expression, Storage? candidatesStorage)
        {
            // ARGUMENTS
            var arguments = new List<Storage>();
            foreach (var argExpression in expression.Arguments)
            {
                var argStorage = argExpression.Accept(this);
                if (argStorage == null)
                    continue;

                arguments.Add(argStorage);
            }

            // RESOLVE FUNCTION
            Function? function;
            Storage? functionStorage;
            if (candidatesStorage?.Type.Tag is FunctionCandidates tagCandidates)
            {
                function = tagCandidates.ResolveSingle(arguments.Select(x => x.Type).ToList());
                functionStorage = function?.Parent.EmitGetForSymbol(function)!;
            }
            else if (candidatesStorage?.Type.Tag is Function tagFunction)
            {
                function = tagFunction;
                functionStorage = function?.Parent.EmitGetForSymbol(function)!;
            }
            else if (candidatesStorage?.Type.Tag is FunctionSignature tagSignature)
            {
                function = new Function(
                    "$anonymous", CurrentModule, this, CallingConvention.Default,
                    tagSignature.Parameters,
                    tagSignature.ReturnType,
                    false
                );
                functionStorage = candidatesStorage;
            }
            else
            {
                function = null;
                functionStorage = null;
            }

            if (function == null)
            {
                messages.Add(Message.NoMatchingFunctionCandidate, expression.FunctionExpression,
                    $"({string.Join(", ", arguments.Select(x => x.Type))})",
                    candidatesStorage?.Type.Tag
                );
                return null;
            }

            // RECTIFY ARGUMENTS
            if (function.CallingConvention == CallingConvention.ThisCall)
            {
                Storage? thisStorage;
                if (function.IsConstructor)
                {
                    thisStorage = CurrentFunction.AllocateRegisterStorage(function.ReturnType);
                    CurrentFunction.Body.Emit(Opcode.New, thisStorage.Operand, function.ReturnType.ToOperand(artifact));
                }
                else if (expression.FunctionExpression is BinaryOperatorExpression { Operator: TokenType.Dot } boe)
                    thisStorage = boe.Left.Accept(this);
                else
                {
                    var thisSymbol = CurrentContext.FindSymbol("this");
                    thisStorage = thisSymbol != null ? CurrentContext.EmitGetForSymbol(thisSymbol) : null;
                }

                if (thisStorage == null)
                {
                    messages.Add(Message.CannotResolveThis, expression.FunctionExpression);
                    return null;
                }

                arguments.Insert(0, thisStorage);
            }

            var parameters = function.Parameters;
            for (int i = 0; i < parameters.Count; ++i)
            {
                var parameter = parameters[i];
                var argument = arguments.ElementAtOrDefault(i);

                // Default Parameter
                if (argument == null)
                    argument = parameter.DefaultValue!.Accept(this);

                // Spread Parameter
                if (parameter.IsSpread)
                {
                    // TODO Cleanup & Merge with ArrayLiteralExpression Visitor
                    int j = i;

                    // RESOLVE TYPE
                    var bType = CobType.U8; // TODO Implement correctly
                    var tag = Intrinsics.Array.TagRecord;
                    tag = tag.FindConcretizedRecord(bType) ?? tag.AllocateConcretizedRecord(bType);
                    var cobType = new CobType(eCobType.Record, tag: tag);

                    if (tag.PopulateConcretizedRecordIfRequired())
                        concreteTypeAstReferences.Add(new ConcreteTypeAstReference(Intrinsics.Array.TagRecord, tag));

                    // ALLOCATE
                    argument = CurrentFunction.AllocateRegisterStorage(cobType);
                    CurrentFunction.Body.Emit(Opcode.New, argument.Operand, cobType.ToOperand(artifact));
            
                    // CALL CTOR
                    var fnSymbol = tag.FindConstructor("New");
                    var fnStorage = tag.EmitGetForSymbol(fnSymbol);

                    CurrentFunction.Body.Emit(
                        Opcode.Call,
                        fnStorage.Operand,
                        Operand.None,
                        new []{ argument.Operand, Operand.ImmediateUnsigned(0) } // TODO Prealloc element count
                    );

                    fnStorage.Free();

                    // ADD ELEMENTS
                    fnSymbol = tag.FindFunction("Add");
                    fnStorage = tag.EmitGetForSymbol(fnSymbol);

                    for (; i < arguments.Count; ++i)
                    {
                        CurrentFunction.Body.Emit(
                            Opcode.Call,
                            fnStorage.Operand,
                            Operand.None,
                            new []{ argument.Operand, arguments[i].Operand }
                        );
                    }

                    fnStorage.Free();

                    while (j < arguments.Count)
                        arguments.RemoveAt(j);
                }

                // Implicit Casting
                if (argument.Type != parameter.Type)
                {
                    if (argument.Operand.Type.IsImmediate()
                    && !CobType.IsImplicitCastable(parameter.Type, argument.Type, argument.Operand.Value))
                        messages.Add(Message.IllegalAssignmentTypeMismatch, expression, argument.Type, parameter.Type);
                    
                    argument = EmitCast(argument, parameter.Type, null);
                }

                // TODO HACK Resolve function candidates to matching signature
                if (argument.Operand == Operand.None && argument.Type.Tag is FunctionCandidates argumentCandidates)
                    argument = function.Parent.EmitGetForSymbol(argumentCandidates.ResolveSingle(null));

                if (i < arguments.Count)
                    arguments[i] = argument;
                else
                    arguments.Add(argument);
            }

            // EMIT CALL
            var retStorage = function.ReturnType != eCobType.None
                ? CurrentFunction.AllocateRegisterStorage(function.ReturnType)
                : null;

            var opcode = function.Parent is RecordType x && x.IsTrait ? Opcode.CallVirt : Opcode.Call;
            CurrentFunction.Body.Emit(
                opcode,
                functionStorage.Operand,
                retStorage?.Operand ?? Operand.None,
                arguments.Select(x => x.Operand).ToList()
            );

            if (function.ReturnType.HasErrorFlag && conditionalStack <= 0 && retStorage != null)
                CurrentFunction.Body.Emit(Opcode.PanicOnErr, retStorage.Operand);

            // CLEANUP
            functionStorage.Free();
            for (int i = 0; i < arguments.Count; ++i)
                CurrentFunction.FreeStorage(arguments[i].Operand);

            return retStorage;
        }

        public Storage? Visit(IdentifierExpression expression)
        {
            var inAssignment = AssignmentRHS != null;

            if (implicitContext != null && expression.Value == "." && !inAssignment)
                return implicitContext;//.Accept(this);

            foreach (var scope in contextStack)
            {
                var symbol = scope.FindSymbol(expression.Value);
                if (symbol == null)
                    continue;

                if (!symbol.IsVisibleTo(CurrentFunction))
                    messages.Add(Message.CannotAccessPrivateSymbol, expression, expression.Value, CurrentFunction.Parent.Name);
                
                if (inAssignment)
                {
                    if (!symbol.Mutable)
                        messages.Add(Message.IllegalAssignmentImmutable, expression);

                    // TODO This definitely feels out of place here...
                    if (isUnionAssignment && AssignmentRHS!.Type != symbol.Type && symbol is ILateTypeBinding lateBinding)
                    {
                        lateBinding.RebindType(new CobType(
                            eCobType.Union,
                            unionedTypes: symbol.Type.YieldTypesInUnion().Append(AssignmentRHS.Type).ToList()
                        ));
                    }
                    else if (!CobType.IsImplicitCastable(symbol.Type, AssignmentRHS.Type, AssignmentRHS.Operand.Value))
                        messages.Add(Message.IllegalAssignmentTypeMismatch, expression, AssignmentRHS.Type, symbol.Type);

                    var result = scope.EmitSetForSymbol(symbol);
                    if (!result)
                        messages.Add(Message.IllegalAssignment, expression);

                    if (mutationStack.Count > 0)
                        mutationStack.Peek().Add(symbol);

                    return null;
                }
                else
                {
                    var result = scope.EmitGetForSymbol(symbol);
                    if (result == null)
                        messages.Add(Message.IllegalAssignment, expression);

                    return result;
                }
            }

            messages.Add(Message.UndeclaredIdentifier, expression, expression.Value);
            return null;
        }

        public Storage? Visit(AheadOfTimeExpression expression)
        {
            var function = new Function(
                "$aot_eval$",
                CurrentModule,
                this,
                CallingConvention.CCall,
                Array.Empty<Function.Parameter>(),
                CobType.None,
                false
            );

            contextStack.Push(function);
            var evalStorage = expression.Expression.Accept(this);
            if (evalStorage == null || evalStorage.Type == eCobType.None)
                messages.Add(Message.AotCannotUseVoid, expression);

            function.ReturnType = evalStorage.Type;
            CurrentFunction.Body.Emit(Opcode.Return, evalStorage.Operand);
            evalStorage.Free();

            function.ReturnLabel.Mark();
            
            contextStack.Pop();

            using var vm = new VirtualMachine(artifact);
            var result = vm.ExecuteFunction(function);

            var reg = CurrentFunction.AllocateRegisterStorage(function.ReturnType);
            CurrentFunction.Body.Emit(
                Opcode.Move,
                reg.Operand,
                Operand.ImmediateSigned(result.IntValue) // TODO Not right
            );
            
            return evalStorage;
        }

        public Storage? Visit(LensExpression expression)
        {
            throw new NotImplementedException();
            //var target = expression.Expression.Accept(this);

            //var storage = CurrentFunction.AllocateRegisterStorage(new CobType(eCobType.Lens, elementType: expression.ElementType));
            //CurrentFunction.Body.Emit(Opcode.Lens, storage.Operand, target.Operand);
            //target.Free();

            //return storage;
        }

        public Storage? Visit(IndexerExpression expression)
        {
            Function? function = null;
            IScopeContext? context = null;
            Operand[]? operandArguments = null;

            var prevAssignmentRHS = AssignmentRHS;
            AssignmentRHS = null;

            var source = expression.Left.Accept(this);
            var index = expression.Index.Accept(this);

            AssignmentRHS = prevAssignmentRHS;

            if (source == null || index == null)
                messages.Add(Message.CannotIndexType, expression, "none");
            else if (AssignmentRHS != null) // SET
            {
                if (source.Type == eCobType.Record
                &&  source.Type.Tag is RecordType recordType 
                &&  recordType.Indexer != null)
                {
                    function = recordType.Indexer.Setter;
                    context = recordType;
                    operandArguments = new []{ source.Operand, index.Operand, AssignmentRHS.Operand };
                }
            }
            else // GET
            {
                var recordType = source.Type.FirstAncestorWhere(
                    x => x == eCobType.Record
                      && x.Tag is RecordType { Indexer: not null }
                )?.Tag as RecordType;

                if (recordType != null)
                {
                    function = index.Type == Intrinsics.Range
                             ? recordType.FindFunction("Slice")
                             : recordType.Indexer.Getter;
                    context = recordType;
                    operandArguments = new []{ source.Operand, index.Operand };
                }
            }

            Storage? storage = null;
            if (function != null && context != null && operandArguments != null)
            {
                var functionStorage = context.EmitGetForSymbol(function);

                if (function.ReturnType.HasErrorFlag && conditionalStack <= 0)
                {
                    storage = CurrentFunction.AllocateRegisterStorage(function.ReturnType.Bust(CobType.Error));
                    CurrentFunction.Body.Emit(Opcode.Call, functionStorage.Operand, storage.Operand, operandArguments);
                    CurrentFunction.Body.Emit(Opcode.PanicOnErr, storage.Operand);
                }
                else
                {
                    storage = CurrentFunction.AllocateRegisterStorage(function.ReturnType);
                    CurrentFunction.Body.Emit(Opcode.Call, functionStorage.Operand, storage.Operand, operandArguments);
                }

                functionStorage.Free(); // function reg
            }
            else
                messages.Add(Message.CannotIndexType, expression, "none");

            index?.Free();
            source?.Free();

            return storage;
        }

        public Storage? Visit(TupleLiteralExpression expression)
        {
            if (AssignmentRHS != null) // SET
            {
                var source = AssignmentRHS.Type.Tag as IScopeContext;
                var prevAssignmentRHS = AssignmentRHS;
                AssignmentRHS = null;

                for (var i = 0; i < expression.Expressions.Count; ++i)
                {
                    var prevBinOpLHS = BinOpLHS;
                    BinOpLHS = prevAssignmentRHS;
                    var symbol = source.FindSymbol($"'{i}");
                    var storage = source.EmitGetForSymbol(symbol);
                    BinOpLHS = prevBinOpLHS;

                    AssignmentRHS = storage;
                    expression.Expressions[i].Accept(this)?.Free();
                    storage?.Free();
                }

                AssignmentRHS = prevAssignmentRHS;

                return null;
            }
            else // GET
            {
                var fieldStorages = new List<Storage?>(expression.Expressions.Count);
                for (var i = 0; i < expression.Expressions.Count; ++i)
                {
                    var fieldStorage = expression.Expressions[i].Accept(this);
                    fieldStorages.Add(fieldStorage);
                }

                CobType cobType;
                if (expression.ExplicitTypeName != null)
                    cobType = CobType.FromTypeName(expression.ExplicitTypeName, CurrentContext);
                else
                {
                    var tupleType = Intrinsics.FindOrAllocateAnonymousTupleType(
                        fieldStorages.Select(x => x.Type).ToList(),
                        this
                    );

                    cobType = new CobType(eCobType.Record, tag: tupleType);
                }

                var storage = CurrentFunction.AllocateRegisterStorage(cobType);
                CurrentFunction.Body.Emit(Opcode.New, storage.Operand, cobType.ToOperand(artifact));

                for (var i = 0; i < expression.Expressions.Count; ++i)
                {
                    var fieldStorage = fieldStorages[i];
                    CurrentFunction.Body.Emit(
                        Opcode.SetField,
                        storage.Operand,
                        Operand.ImmediateUnsigned(i),
                        fieldStorage.Operand
                    );
                }

                return storage;
            }
        }

        public Storage? Visit(StructLiteralExpression expression)
        {
            //var structTypeStorage = expression.StructTypeExpression.Accept(this);
            //if (structTypeStorage == null || structTypeStorage.Type != eCobType.Struct
            //||  structTypeStorage.Type.Tag is not RecordType structType
            //||  structType.Type != eRecordType.Struct)
            //{
            //    messages.Add(Message.CannotInstantiateType, expression.StructTypeExpression, structTypeStorage?.Type);
            //    return null;
            //}

            //var cobType = new CobType(eCobType.Struct, tag: structType);

            var structTypeStorage = expression.StructTypeExpression.Accept(this);
            var structType = structTypeStorage?.Type.FirstAncestorWhere(x => /* TODO x.Type == eCobType.Struct && */x.Tag is RecordType);
            if (structType == null)
            {
                messages.Add(Message.CannotInstantiateType, expression.StructTypeExpression, structTypeStorage?.Type);
                return null;
            }

            var mutations = new HashSet<ISymbol>();
            mutationStack.Push(mutations);

            var cobType = structTypeStorage.Type;

            // ALLOCATE
            var storage = CurrentFunction.AllocateRegisterStorage(cobType);
            CurrentFunction.Body.Emit(Opcode.New, storage.Operand, cobType.ToOperand(artifact));
            
            // INVOKE DEFAULT CONSTRUCTOR
            var recordType = (RecordType)structType.Tag!;
            var ctorSymbol = recordType.FindConstructor(null);
            if (ctorSymbol != null)
            {
                var ctorStorage = recordType.EmitGetForSymbol(ctorSymbol);
                CurrentFunction.Body.Emit(Opcode.Call, ctorStorage.Operand, Operand.None, new[] { storage.Operand });
                ctorStorage.Free();

                foreach (var x in ctorSymbol.MutatedSymbols) mutations.Add(x);
            }

            // INITIALIZER ASSIGNMENTS
            if (expression.Assignments != null)
            {
                var prevIsDivingInitializer = IsDivingInitializer;
                IsDivingInitializer = true;
                var prevBinOpLHS = BinOpLHS;
                BinOpLHS = storage;
                contextStack.Push(recordType);
                for (var i = 0; i < expression.Assignments.Count; ++i)
                    expression.Assignments[i].Accept(this)?.Free();
                contextStack.Pop();
                BinOpLHS = prevBinOpLHS;
                IsDivingInitializer = prevIsDivingInitializer;
            }

            // VALIDATE ALL FIELDS INITIALIZED
            if (!recordType.IsFullyInitialized(mutations))
                messages.Add(Message.NotAllFieldsInitialized, expression);

            mutationStack.Pop();

            // ENFORCE REFINEMENT
            if (cobType.Refinement != null)
            {
                var storage2 = CurrentFunction.AllocateRegisterStorage(cobType);
                CurrentFunction.Body.Emit(Opcode.Move, storage2.Operand, storage.Operand);
                EnforceRefinment(storage2, cobType, expression);
                storage2.Free();
            }

            return storage;
        }

        public Storage? Visit(ArrayLiteralExpression expression)
        {
            Function? function;
            Storage? functionStorage;

            var elements = new List<(bool IsSpread, Storage? Storage)>();
            foreach (var element in expression.Elements)
            {
                if (element is PrefixOperatorExpression { Operator: TokenType.Spread } poe)
                    elements.Add((true, poe.Right.Accept(this)));
                else
                    elements.Add((false, element.Accept(this)));
            }

            // RESOLVE TYPE
            var bType = expression.ElementTypeName == null
                      ? CobType.U8 // TODO This needs to infer the type from the elements, implement in the future
                      : CobType.FromTypeName(expression.ElementTypeName, CurrentContext);
            var tag = Intrinsics.Array.TagRecord;
            tag = tag.FindConcretizedRecord(bType) ?? tag.AllocateConcretizedRecord(bType);
            var cobType = new CobType(eCobType.Record, tag: tag);

            if (tag.PopulateConcretizedRecordIfRequired())
                concreteTypeAstReferences.Add(new ConcreteTypeAstReference(Intrinsics.Array.TagRecord, tag));

            // ALLOCATE
            var storage = CurrentFunction.AllocateRegisterStorage(cobType);
            CurrentFunction.Body.Emit(Opcode.New, storage.Operand, cobType.ToOperand(artifact));

            // CALL CTOR
            Storage? countStorage;
            if (expression.CountExpression != null)
            {
                // Fill Elements
                // TODO Validate types match
                countStorage = expression.CountExpression.Accept(this);
                var countValue = countStorage.Operand.Type.IsImmediate() ? countStorage.Operand.Value : -1;

                if (!elements.Any(x => x.IsSpread && x.Storage.Operand.Type.IsImmediate()) && countValue != -1 && countValue != elements.Count)
                {
                    messages.Add(Message.FixedArrayDimensionMismatch, expression, countValue, elements.Count);
                    return null;
                }

                // ALLOCATE
                function = tag.FindConstructor("New");
                functionStorage = tag.EmitGetForSymbol(function);

                CurrentFunction.Body.Emit(
                    Opcode.Call,
                    functionStorage.Operand,
                    Operand.None,
                    new []{ storage.Operand, countStorage.Operand }
                );
                functionStorage.Free();
            }
            else
            {
                // Explicit Elements
                // TODO Validate types match

                function = tag.FindConstructor("New");
                functionStorage = tag.EmitGetForSymbol(function);

                CurrentFunction.Body.Emit(
                    Opcode.Call,
                    functionStorage.Operand,
                    Operand.None,
                    new []{ storage.Operand, Operand.ImmediateUnsigned(elements.Count) }
                );
                functionStorage.Free();

                countStorage = null;
            }

            // ADD ELEMENTS
            for (var i = 0; i < elements.Count; ++i)
            {
                var element = elements[i];

                IReadOnlyList<Operand> arguments;
                if (element.IsSpread)
                {
                    if (element.Storage.Type.Type is eCobType.Unsigned or eCobType.Signed or eCobType.Real)
                    {
                        if (countStorage == null)
                        {
                            messages.Add(Message.CannotSpreadNumberHere, expression);
                            return null;
                        }

                        var repeatStorage = CurrentFunction.AllocateRegisterStorage(CobType.Int);
                        CurrentFunction.Body.Emit(Opcode.Move, repeatStorage.Operand, countStorage.Operand);
                        CurrentFunction.Body.Emit(Opcode.Sub, repeatStorage.Operand, repeatStorage.Operand, Operand.ImmediateUnsigned(i));

                        function = tag.FindFunction("AddRangeLiteral"); // TODO Find a better name
                        functionStorage = tag.EmitGetForSymbol(function);
                        arguments = new[] { storage.Operand, element.Storage.Operand, repeatStorage.Operand };
                        repeatStorage.Free();
                    }
                    else
                    {
                        function = tag.FindFunction("AddRange");
                        functionStorage = tag.EmitGetForSymbol(function);
                        arguments = new[] { storage.Operand, element.Storage.Operand };
                    }
                }
                else
                {
                    function = tag.FindFunction("Add");
                    functionStorage = tag.EmitGetForSymbol(function);
                    arguments = new[] { storage.Operand, element.Storage.Operand };
                }

                CurrentFunction.Body.Emit(
                    Opcode.Call,
                    functionStorage.Operand,
                    Operand.None,
                    arguments
                );

                functionStorage.Free();
                element.Storage.Free();
            }

            countStorage?.Free();

            return storage;
        }

        public Storage? Visit(NumberLiteralExpression expression)
        {
            var type = CobType.FromTypeName(expression.TypeName, CurrentContext);
            return new Storage(type, Operand.Immediate(expression.LongValue, type));
        }

        public Storage? Visit(BooleanLiteralExpression expression)
        {
            return new Storage(
                CobType.Boolean,
                Operand.ImmediateUnsigned(expression.Value ? 1 : 0)
            );
        }

        public Storage? Visit(StringLiteralExpression expression)
        {
            var global = Intrinsics.AllocateStringLiteral(expression.Value, this);
            var idx = Globals.IndexOf(global);
            
            return new Storage(
                CobType.String,
                Operand.Global(idx)
            );
        }

        public Storage? Visit(CharacterLiteralExpression expression)
        {
            return new Storage(
                CobType.U8,
                Operand.ImmediateUnsigned(expression.Value)
            );
        }

        public Storage? Visit(NilLiteralExpression expression)
        {
            return new Storage(CobType.Nil, CobType.Nil.ToOperand(artifact));
        }

        public Storage? Visit(EmptyExpression expression)
        {
            return null;
        }

        public void GenerateFunctionStringErrorBody()
        {
            if (FunctionStringError.Body.Instructions.Count > 0)
                throw new InvalidOperationException();

            var nextLabel = FunctionStringError.Body.AllocateLabel();

            // TODO Optimize implementation to a jump table, probably
            var errorCodes = Globals.Where(x => x.Type == eCobType.Error).ToList();
            foreach (var errorCode in errorCodes)
            {
                var cmpStorage = FunctionStringError.AllocateRegisterStorage(CobType.Boolean);
                FunctionStringError.Body.Emit(
                    Opcode.CmpEQ,
                    cmpStorage.Operand,
                    Operand.Argument(0),
                    Operand.ImmediateSigned(errorCode.IntValue)
                    //Operand.Global(Globals.IndexOf(errorCode))
                );
                FunctionStringError.Body.Emit(
                    Opcode.JmpF,
                    cmpStorage.Operand,
                    nextLabel
                );
                cmpStorage.Free();
                var global = Intrinsics.AllocateStringLiteral($"error.{errorCode.Name}", this);
                FunctionStringError.Body.Emit(
                    Opcode.Return,
                    Operand.Global(Globals.IndexOf(global))
                );

                nextLabel.Mark();
                nextLabel = FunctionStringError.Body.AllocateLabel();
            }

            var defaultGlobal = Intrinsics.AllocateStringLiteral("error", this);
            FunctionStringError.Body.Emit(
                Opcode.Return,
                Operand.Global(Globals.IndexOf(defaultGlobal))
            );
        }

        // NOTE Takes ownership of source; discards if needed
        // TODO Expression? expression is super weird here
        private Storage EmitCast(Storage source, CobType dstType, Expression? expression)
        {
            var srcType = source.Type;
            if (srcType == dstType)
                return source;
            
            // NOTE Emit instructions to handle casts as needed

            // INTEGERS
            if (srcType.Type is eCobType.Unsigned or eCobType.Signed or eCobType.Real
            &&  dstType.Type is eCobType.Unsigned or eCobType.Signed or eCobType.Real)
            {
                if (source.Operand.Type.IsImmediate()
                &&  CobType.IsIntegerTypeCapableOfStoringValueWithoutTruncation(dstType, srcType, source.Operand.Value))
                {
                    return new Storage(
                        dstType,
                        Operand.Immediate(source.Operand.Value, dstType)
                    );
                }
                else
                {
                    var destination = CurrentFunction.AllocateRegisterStorage(dstType);
                    CurrentFunction.Body.Emit(
                        Opcode.Cast,
                        destination.Operand,
                        source.Operand,
                        dstType.ToOperand(artifact)
                    );
                    source.Free();
                    return destination;
                }
            }

            // REFINEMENTS
            EnforceRefinment(source, dstType, expression);

            return new Storage(source, dstType);
        }

        private void EnforceRefinment(Storage source, CobType dstType, Expression? expression)
        {
            if (dstType.Refinement == null)
                return;

            if (source.Operand.Type.IsImmediate() && IsCompileTimeResolvable(dstType.Refinement))
            {
                // TODO Could probably merge this into a common function for AOT eval
                var function = new Function(
                    "$aot_eval$",
                    CurrentModule,
                    this,
                    CallingConvention.CCall,
                    Array.Empty<Function.Parameter>(),
                    CobType.None,
                    false
                );

                contextStack.Push(function);

                var prevImplicitContext = implicitContext;
                implicitContext = source;

                ++conditionalStack;
                var evalStorage = dstType.Refinement.Accept(this);
                --conditionalStack;

                implicitContext = prevImplicitContext;

                if (evalStorage == null || evalStorage.Type != eCobType.Boolean)
                    messages.Add(Message.RefinementMustResolveToBoolean, dstType.Refinement);

                function.ReturnType = evalStorage.Type;
                CurrentFunction.Body.Emit(Opcode.Return, evalStorage.Operand);
                evalStorage.Free();

                function.ReturnLabel.Mark();
            
                contextStack.Pop();

                using var vm = new VirtualMachine(artifact);
                var result = vm.ExecuteFunction(function);

                if (result == null || result.IntValue != 1)
                    messages.Add(Message.RefinementViolated, expression);
            }
            else
            {
                var prevImplicitContext = implicitContext;
                implicitContext = source;

                ++conditionalStack;
                var result = dstType.Refinement.Accept(this);
                --conditionalStack;

                if (result == null || result.Type != eCobType.Boolean)
                    messages.Add(Message.RefinementMustResolveToBoolean, dstType.Refinement);

                var labelLegal = CurrentFunction.Body.AllocateLabel();
                CurrentFunction.Body.Emit(Opcode.JmpT, result.Operand, labelLegal);
                CurrentFunction.Body.Emit(Opcode.PanicOnErr, Operand.Global(Globals.IndexOf(Globals.First(x => x.Type == eCobType.Error && x.Name == "RefinementViolation"))));
                labelLegal.Mark();
                result.Free();

                implicitContext = prevImplicitContext;
            }
        }

        public static bool IsSymbolVisible(IScopeContext? context, IScopeContext? parent, string? name)
        {
            for (var ctx = context; ctx != null; ctx = ctx.Parent)
            {
                if (ctx == parent)
                    return true;
            }

            return !string.IsNullOrEmpty(name) && char.IsUpper(name[0]);
        }

        public static bool IsCompileTimeResolvable(Expression expression)
        {
            // TODO Not an exhaustive heuristic
            return expression is BinaryOperatorExpression boe
                && boe.Operator is TokenType.Equals or TokenType.NotEquals
                                or TokenType.LessThan or TokenType.LessThanOrEquals
                                or TokenType.MoreThan or TokenType.MoreThanOrEquals
                                or TokenType.ConditionalAnd or TokenType.ConditionalOr;
        }

        public static Artifact Compile(ScriptExpression ast, MessageCollection messages)
        {
            try
            {
                stopwatch.Start();

                var artifact = new Artifact();
                var compiler = new Compiler(artifact, messages);

                // PASS 0 - Forward Declarations
                Intrinsics.InitializeForPass0(compiler);

                for (var phase = ForwardDeclaration.DeclPhase.Begin;
                     phase < ForwardDeclaration.DeclPhase.Complete;
                     ++phase
                ) {
                    var pass0 = new ForwardDeclaration(phase, compiler, messages);

                    for (var i = 0; i < compiler.Scripts.Count; ++i)
                        compiler.Scripts[i].Accept(pass0);

                    ast.Accept(pass0);
                }

                // PASS 1 - Compilation
                // NOTE We must compile all imported scripts before we compile this script; list is in import order
                Intrinsics.InitializeForPass1(compiler);
                compiler.FunctionStringError = compiler.RootModule.AllocateFunction("string", CallingConvention.Default, new[] { new Function.Parameter("code", CobType.Error, false) }, CobType.String);

                for (var i = 0; i < compiler.Scripts.Count; ++i)
                    compiler.Scripts[i].Accept(compiler);
                
                ast.Accept(compiler);

                // HACK Really shouldn't be needed here
                foreach (var module in artifact.Modules)
                    module.InitializerFunction.Body.Emit(Opcode.Return);

                compiler.GenerateFunctionStringErrorBody();

                
                // PASS 2 - Generics
                var hack = compiler.concreteTypeAstReferences.ToList();
                var seen = new List<ConcreteTypeAstReference>();
                while (hack.Count > 0)
                {
                    foreach (var concreteType in hack)
                    {
                        var genericType = compiler.genericTypeAstReferences.First(x => x.Generic == concreteType.Generic);
                        Expression concreteExpression = genericType.Expression switch
                        {
                            RecordStatement x => new RecordStatement(x, concreteType.Concrete.Name),
                            _ => throw new ArgumentOutOfRangeException($"Unknown generic type expression '{genericType.Expression.Token}'")
                        };

                        compiler.contextStack.Clear();
                        foreach (var y in genericType.ContextStack)
                            compiler.contextStack.Push(y);
                        
                        concreteExpression.Accept(compiler);
                    }

                    seen.AddRange(hack);
                    hack = seen.Except(hack).ToList();
                }
                
                return artifact;
            }
            finally
            {
                stopwatch.Stop();
            }
        }

        public static long TotalMilliseconds => stopwatch.ElapsedMilliseconds;
        private static readonly Stopwatch stopwatch = new ();

        private sealed record GenericTypeAstReference(IScopeContext Generic, Expression Expression, Stack<IScopeContext> ContextStack);

        private sealed record ConcreteTypeAstReference(IScopeContext Generic, IScopeContext Concrete);

        private sealed class RootScopeContext : IScopeContext
        {
            private static readonly RootSymbol ForSymbol = new (CobType.None, false);
            private static readonly RootSymbol PanicSymbol = new (new CobType(eCobType.Function, tag: new FunctionSignature()), true);

            public string Name => "(root)";

            public IScopeContext? Parent => null;

            private readonly Compiler compiler;

            public RootScopeContext(Compiler compiler)
            {
                this.compiler = compiler;
            }

            public ISymbol? FindSymbol(string name)
            {
                // TODO Move '.' implicit context here
                return name switch
                {
                    "for" => ForSymbol,
                    "panic" => PanicSymbol,
                    _ => null
                };
            }

            public Storage? EmitGetForSymbol(ISymbol symbol)
            {
                if (symbol == ForSymbol && compiler.forTempBlockScope != null)
                    return new Storage(new CobType(eCobType.None, tag: compiler.forTempBlockScope), Operand.None);

                if (symbol == PanicSymbol)
                {
                    compiler.CurrentFunction.Body.Emit(
                        Opcode.PanicOnErr,
                        Operand.Global(compiler.Globals.IndexOf(compiler.Globals.First(x => x.Type == eCobType.Error && x.Name == "Panic")))
                    );
                    return new Storage(CobType.None, Operand.None);
                }
                return null;
            }

            public bool EmitSetForSymbol(ISymbol symbol)
            {
                if (symbol == PanicSymbol)
                {
                    Storage? storage;
                    if (compiler.AssignmentRHS?.Type.TagFunctionCandidates != null)
                    {
                        storage = compiler.CurrentModule.EmitGetForSymbol(
                            compiler.AssignmentRHS.Type.TagFunctionCandidates.ResolveSingle(null)
                        );
                    }
                    else
                        storage = null;

                    if (storage != null)
                    {
                        compiler.CurrentFunction.Body.Emit(
                            Opcode.SetPanicHandler,
                            storage.Operand
                        );
                        storage.Free();
                    }

                    return true;
                }

                return false;
            }

            private sealed class RootSymbol : ISymbol
            {
                public CobType Type { get; }

                public bool Mutable { get; }

                public bool IsVisibleTo(IScopeContext context) => true;

                public RootSymbol(CobType type, bool mutable)
                {
                    Type = type;
                    Mutable = mutable;
                }
            }
        }
    }

    internal interface IScopeContext
    {
        string Name { get; }

        IScopeContext? Parent { get; }

        ISymbol? FindSymbol(string name); // TODO FindExplicitSymbol

        //ISymbol? FindImplicitSymbol(eCobType containerType, IReadOnlyList<CobType> fieldTypes);

        Storage? EmitGetForSymbol(ISymbol symbol);

        bool EmitSetForSymbol(ISymbol symbol); // TODO EmitSetForSymbol(ISymbol symbol, Storage value)
    }

    internal interface ISymbol
    {
        CobType Type { get; }

        bool Mutable { get; }

        bool IsVisibleTo(IScopeContext context);
    }

    internal sealed record LoopContext(string? Tag, Label Continue, Label Break);

    // TODO Move into a seperate file
    internal sealed class EnumRefinement : IScopeContext, ILateTypeBinding
    {
        public string Name { get; }

        public IScopeContext? Parent { get; }

        public CobType ThisType { get; private set; }

        private readonly List<Member> members;

        private readonly Compiler compiler;

        public EnumRefinement(
            string name,
            IScopeContext parent,
            Compiler compiler
        ) {
            Name = name;
            Parent = parent;

            members = new List<Member>();

            this.compiler = compiler;
        }

        public void AllocateMember(string name, object value)
        {
            Operand operand;
            if (value is Operand vOperand)
                operand = vOperand;
            else if (value is long longValue)
                operand = Operand.Immediate(longValue, ThisType.Parent!);
            else if (value is string stringValue)
            {
                var global = Intrinsics.AllocateStringLiteral(stringValue, compiler);
                var idx = compiler.Globals.IndexOf(global);
                operand = Operand.Global(idx);
            }
            else
                throw new InvalidOperationException();

            members.Add(new Member(name, operand, this));
        }

        public Member? FindMember(string name)
        {
            return members.FirstOrDefault(x => x.Name == name);
        }

        public ISymbol? FindSymbol(string name)
        {
            Member? member;
            if ((member = FindMember(name)) != null)
                return member;

            return null;
        }

        public Storage? EmitGetForSymbol(ISymbol symbol)
        {
            if (symbol is Member member)
                return new Storage(ThisType, member.Value);

            return null;
        }

        public bool EmitSetForSymbol(ISymbol symbol)
        {
            return false;
        }

        void ILateTypeBinding.RebindType(CobType type)
        {
            if (ThisType != null!)
                throw new InvalidOperationException();

            ThisType = type;
        }

        public sealed class Member : ISymbol
        {
            public string Name { get; }

            public Operand Value { get; }

            public CobType Type => parent.ThisType;

            public bool Mutable => false;

            private readonly EnumRefinement parent;

            public Member(string name, Operand value, EnumRefinement parent)
            {
                Name = name;
                Value = value;
                this.parent = parent;
            }

            public bool IsVisibleTo(IScopeContext context) => true;
        }
    }
}
