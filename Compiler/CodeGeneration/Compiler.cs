using System.Collections;
using Compiler.Ast;
using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Ast.Visitors;
using Compiler.CodeGeneration.Artifacts;
using Compiler.Interpreter;
using Compiler.Lexer;
using System.Diagnostics;
using System.Text;

namespace Compiler.CodeGeneration
{
    internal sealed class Compiler : IExpressionVisitor<Storage?>
    {
        public List<ArtifactDirective> ArtifactDirectives => artifact.ArtifactDirectives;

        public List<Import> Imports => artifact.Imports;

        public List<Export> Exports => artifact.Exports;

        public List<Module> Modules => artifact.Modules;

        public List<TraitType> TraitTypes => artifact.TraitTypes;

        public List<TupleType> TupleTypes => artifact.TupleTypes;

        public List<StructType> StructTypes => artifact.StructTypes;

        public List<Function> Functions => artifact.Functions;

        public List<Variable> Globals => artifact.Globals;

        public List<ScriptExpression> Scripts { get; }

        public Module RootModule { get; }

        public Module Errors { get; }

        public IScopeContext CurrentContext => contextStack.Peek();

        public Module CurrentModule => contextStack.OfType<Module>().First();

        public Function CurrentFunction => contextStack.OfType<Function>().First();

        private Expression? implicitContext;

        private readonly List<GenericTypeAstReference> genericTypeAstReferences;
        private readonly List<ConcreteTypeAstReference> concreteTypeAstReferences;
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
            contextStack = new Stack<IScopeContext>();
            loopStack = new Stack<LoopContext>();

            Modules.Add(RootModule);
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

        public Storage? Visit(TypeAliasStatement expression)
        {
            return null;
        }

        public Storage? Visit(TraitStatement expression)
        {
            return null;
        }

        public Storage? Visit(ErrorStatement expression)
        {
            return null;
        }

        public Storage? Visit(TupleDeclStatement expression)
        {
            var tupleType = CurrentModule.FindTupleType(expression.Name)!;
            if (tupleType.IsGeneric)
            {
                genericTypeAstReferences.Add(new GenericTypeAstReference(
                    tupleType,
                    expression,
                    new Stack<IScopeContext>(contextStack)
                ));
            }
            else
            {
                contextStack.Push(tupleType);

                foreach (var functionExpression in expression.Functions)
                    functionExpression.Accept(this);

                foreach (var fieldExpression in expression.Fields)
                {
                    var field = tupleType.FindField(fieldExpression.Name);

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

                if (tupleType.Indexer != null)
                {
                    if (tupleType.Indexer.Getter != null)
                    {
                        contextStack.Push(tupleType.Indexer.Getter);
                        
                        var retStorage = expression.Indexer?.GetterExpression?.Accept(this);
                        if (retStorage != null)
                        {
                            CurrentFunction.Body.Emit(Opcode.Return, retStorage.Operand);
                            retStorage.Free();
                        }

                        contextStack.Pop();
                    }

                    if (tupleType.Indexer.Setter != null)
                    {
                        contextStack.Push(tupleType.Indexer.Setter);
                        expression.Indexer?.SetterExpression?.Accept(this)?.Free();
                        CurrentFunction.Body.Emit(Opcode.Return, Operand.ImmediateUnsigned(0));
                        contextStack.Pop();
                    }
                }

                contextStack.Pop();
            }

            return null;
        }

        public Storage? Visit(StructDeclStatement expression)
        {
            var structType = CurrentModule.FindStructType(expression.Name)!;
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

                foreach (var factoryExpression in expression.Factories)
                    factoryExpression.Accept(this);

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

            return null;
        }

        public Storage? Visit(FactoryDeclStatement expression)
        {
            Function function;
            if (CurrentContext is StructType structType)
                function = structType.FindFactory(expression.Name)!;
            else
                throw new InvalidOperationException(); // TODO ??

            contextStack.Push(function);

            expression.Body.Accept(this);

            contextStack.Pop();

            return new Storage(
                new CobType(eCobType.Function, tag: function),
                Operand.None
            );
        }

        public Storage? Visit(FunctionDeclStatement expression)
        {
            if (expression.Body == null)
            {
                messages.Add(Message.MissingFunctionBody, expression);
                return null;
            }

            // TODO Make a clean API for this
            Function function;
            if (CurrentContext is StructType structType)
                function = structType.FindFunction(expression.Name)!;
            else if (CurrentContext is TupleType tupleType)
                function = tupleType.FindFunction(expression.Name)!;
            else
            {
                var candidates = CurrentModule.FindFunctionCandidates(expression.Name)!;
                var parameterTypes = expression.Parameters.Select(x => CobType.FromString(x.TypeName, CurrentContext)).ToList();
                function = candidates.ResolveSingle(parameterTypes)!;
                //function = CurrentModule.FindFunction(expression.Name)!;
            }

            if (function == null) throw new InvalidOperationException("Unable to resolve declared function");

            contextStack.Push(function);

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
                if (context is StructType structType)
                    field = structType.FindField(expression.Field.Name);
                else if (context is TupleType tupleType)
                    field = tupleType.FindField(expression.Field.Name);
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
            for (var i = 0; i < expression.Declarations.Count; ++i)
            {
                var decl = expression.Declarations[i];
                var mutable = expression.Type == TokenType.Var;
                var type = decl.Type;

                Storage? rhs;
                if (decl.Initializer != null)
                {
                    rhs = decl.Initializer.Accept(this);
                    if (rhs == null)
                        messages.Add(Message.TypeMismatch, expression, "any", "none");
                    else if (type != null && !CobType.IsCastable(rhs.Type, type))
                        messages.Add(Message.TypeMismatch, expression, type, rhs.Type);
                    else
                        type = rhs.Type;
                }
                else
                    rhs = null;

                if ((decl.Type == null && decl.Initializer == null)
                ||  type == null)
                {
                    messages.Add(Message.MalformedVarDeclaration, expression);
                    continue;
                }
                
                if (CurrentFunction == CurrentModule.InitializerFunction) // Global Decl
                {
                    var global = CurrentModule.FindGlobal(decl.Name)!;
                    var idx = Globals.IndexOf(global);
                    if (rhs != null)
                    {
                        CurrentFunction.Body.Emit(
                            Opcode.Move,
                            Operand.Global(idx),
                            rhs.Operand
                        );
                    }
                }
                else
                {
                    var local = CurrentFunction.AllocateLocal(decl.Name, type, mutable);
                    var idx = CurrentFunction.FindLocalIndex(local);
                    if (rhs != null)
                    {
                        CurrentFunction.Body.Emit(
                            Opcode.Move,
                            Operand.Local(idx),
                            rhs.Operand
                        );
                    }
                }

                rhs?.Free();
            }

            return null;
        }

        public Storage? Visit(IfStatement expression)
        {
            ++conditionalStack;
            var conditional = expression.Conditional.Accept(this);
            --conditionalStack;

            if (conditional == null || conditional.Type != CobType.Boolean)
            {
                messages.Add(Message.TypeMismatch, expression.Conditional, CobType.Boolean, conditional?.Type.ToString() ?? "none");
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

            expression.Then.Accept(this)?.Free();
            CurrentFunction.Body.Emit(Opcode.Jmp, endLabel);

            elseLabel.Mark();

            expression.Else?.Accept(this)?.Free();

            endLabel.Mark();

            return null;
        }

        public Storage? Visit(ForStatement expression)
        {
            var startLabel = CurrentFunction.Body.AllocateLabel();
            var endLabel = CurrentFunction.Body.AllocateLabel();

            loopStack.Push(new LoopContext(expression.Label?.Value, startLabel, endLabel));

            if (expression.Conditional is BinaryOperatorExpression boe
            &&  boe.Operator == TokenType.In)
            {
                var index = boe.Left.Accept(this);
                var enumerableStorage = boe.Right.Accept(this);

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
                    messages.Add(Message.CannotEnumerateType, boe.Right, enumerableStorage?.Type.ToString() ?? "(null)");
                    return null;
                }

                var enumeratorType = getEnumeratorFn.Type.TagFunction.ReturnType;
                if (enumeratorType.Tag is StructType structType)
                {
                    var superType = structType.HACK_PendingSuperType;
                    if (structType.PopulateConcretizedStructIfRequired())
                        concreteTypeAstReferences.Add(new ConcreteTypeAstReference(superType, structType));
                }
                else if (enumeratorType.Tag is TupleType tupleType)
                {
                    var superType = tupleType.HACK_PendingSuperType;
                    if (tupleType.PopulateConcretizedTupleIfRequired())
                        concreteTypeAstReferences.Add(new ConcreteTypeAstReference(superType, tupleType));
                }

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
                    messages.Add(Message.CannotEnumerateType, boe.Right, enumerableStorage?.Type.ToString() ?? "(null)");
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

                // index = $tmp.Current;

                //Storage? currentField;
                //if (enumeratorStorage.Type.Tag is IScopeContext enumeratorScopeContext2)
                //{
                //    var currentFieldSymbol = enumeratorScopeContext2.FindSymbol("Current");
                //    currentField = currentFieldSymbol != null
                //        ? enumeratorScopeContext2.EmitGetForSymbol(currentFieldSymbol)
                //        : null;
                //}
                //else
                //    currentField = null;

                //if (currentField == null)
                //{
                //    messages.Add(Message.CannotEnumerateType, boe.Right, enumerableStorage?.Type.ToString() ?? "(null)");
                //    return null;
                //}

                //CurrentFunction.Body.Emit(Opcode.Move, index.Operand, currentField.Operand);
                //currentField.Free();

                CurrentFunction.Body.Emit(Opcode.GetField, index.Operand, enumeratorStorage.Operand, Operand.ImmediateUnsigned(0));

                index.Free();
            }
            else if (expression.Conditional != null)
            {
                startLabel.Mark();
                var a = expression.Conditional.Accept(this);
                CurrentFunction.Body.Emit(Opcode.JmpF, a.Operand, endLabel);
                a.Free();
            }
            else
                startLabel.Mark();

            expression.Body.Accept(this)?.Free();

            CurrentFunction.Body.Emit(Opcode.Jmp, startLabel);
            endLabel.Mark();

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
                else if (!CobType.IsCastable(rhs.Type, CurrentFunction.ReturnType))
                    messages.Add(Message.ReturnTypeMismatch, expression, CurrentFunction.ReturnType, rhs.Type);
                
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
            for (var i = 0; i < expression.Expressions.Count; i++)
            {
                var expr = expression.Expressions[i];

                implicitContext = null;

                var retStorage = expr.Accept(this);
                if (expr is FatArrowStatement)
                    return retStorage;

                retStorage?.Free();
            }

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

        public Storage? Visit(BinaryOperatorExpression expression)
        {
            // Assignment
            var asnOpcode = expression.Operator switch
            {
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
                TokenType.Assign => Opcode.Move,
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
                var bType = rhs.Value == "T" ? CobType.U8 : CobType.FromString(rhs.Value); // HACK TODO THIS IS ENTIRELY INCORRECT
                CobType cType;

                if (aType?.Tag is TupleType tupleType)
                {
                    var tag = tupleType.FindConcretizedTuple(bType) ?? tupleType.AllocateConcretizedTuple(bType);

                    cType = new CobType(eCobType.Tuple, tag: tag);

                    if (tag.PopulateConcretizedTupleIfRequired())
                        concreteTypeAstReferences.Add(new ConcreteTypeAstReference(tupleType, tag));
                }
                else if (aType?.Tag is StructType structType)
                {
                    var tag = structType.FindConcretizedStruct(bType) ?? structType.AllocateConcretizedStruct(bType);

                    cType = new CobType(eCobType.Struct, tag: tag);

                    if (tag.PopulateConcretizedStructIfRequired())
                        concreteTypeAstReferences.Add(new ConcreteTypeAstReference(structType, tag));
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
                BinOpLHS = lhs;

                if (lhs?.Type.Tag is not IScopeContext context)
                {
                    messages.Add(Message.CannotDereferenceContext, expression, lhs?.Type.ToString() ?? "(null)");
                    return null;
                }

                if (context is StructType structType)
                {
                    if (structType.PopulateConcretizedStructIfRequired())
                        concreteTypeAstReferences.Add(new ConcreteTypeAstReference(structType.HACK_PendingSuperType, structType));
                }
                else if (context is TupleType tupleType)
                {
                    if (tupleType.PopulateConcretizedTupleIfRequired())
                        concreteTypeAstReferences.Add(new ConcreteTypeAstReference(tupleType.HACK_PendingSuperType, tupleType));
                }

                implicitContext = expression.Left;
                contextStack.Push(context);

                var rhs = expression.Right.Accept(this);
                
                contextStack.Pop();
                lhs?.Free();

                BinOpLHS = null;
                return rhs;
            }
            else if (expression.Operator == TokenType.BitPack) // BitPack <<|
            {
                var a = expression.Left.Accept(this);
                var b = expression.Right.Accept(this);
                var c = CurrentFunction.AllocateRegisterStorage(a.Type);

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
                var type = Intrinsics.Range;
                var cobType = new CobType(eCobType.Tuple, tag: type);

                var c = CurrentFunction.AllocateRegisterStorage(cobType);
                CurrentFunction.Body.Emit(Opcode.New, c.Operand, cobType.ToOperand(artifact));

                var a = expression.Left.Accept(this);
                var b = expression.Right.Accept(this);

                var aType = a?.Type ?? b?.Type ?? CobType.UInt;
                var bType = b?.Type ?? a?.Type ?? CobType.UInt;

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
                var c = CurrentFunction.AllocateRegisterStorage(CobType.Boolean);

                CurrentFunction.Body.Emit(cndOpcode, c.Operand, a.Operand, b.Operand);

                b.Free();
                a.Free();

                return c;
            }
            else if (cmpOpcode != Opcode.None) // Equality == != < > <= >=
            {
                var a = expression.Left.Accept(this);
                var b = expression.Right.Accept(this);
                var c = CurrentFunction.AllocateRegisterStorage(CobType.Boolean);

                CurrentFunction.Body.Emit(cmpOpcode, c.Operand, a.Operand, b.Operand);

                b.Free();
                a.Free();

                return c;
            }
            else if (artOpcode != Opcode.None) // Arithmetic + - * /
            {
                var a = expression.Left.Accept(this);
                var b = expression.Right.Accept(this);
                var c = CurrentFunction.AllocateRegisterStorage(a.Type);
                
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
                }
                else
                {
                    AssignmentRHS = b;
                    c = null;
                }

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
                    messages.Add(Message.CannotDereferenceContext, expression, "(null)");
                    return null;
                }

                var prevAsnSrc = AssignmentRHS;
                AssignmentRHS = null;
                var lhs = implicitContext.Accept(this);
                AssignmentRHS = prevAsnSrc;
                BinOpLHS = lhs;

                contextStack.Push((IScopeContext)lhs!.Type.Tag!);
                var b = expression.Right.Accept(this);
                contextStack.Pop();

                lhs.Free();
                BinOpLHS = null;

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
                var type = expression.RightSingle.Type;

                var c = CurrentFunction.AllocateRegisterStorage(CobType.Boolean);

                CurrentFunction.Body.Emit(
                    expression.Operation == TokenType.Is ? Opcode.CmpTyEQ : Opcode.CmpTyNEQ,
                    c.Operand,
                    lhs.Operand,
                    type.ToOperand(artifact)
                );

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

                    var isDefaultBranch = branch.TypeName == "default";
                    var useDynamicTypeCheck = lhs.Type == eCobType.Union;

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
                    else if (branch.Type != null)
                        type = branch.Type;
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

                    if (resultType == null || CobType.IsCastable(resultType, rhs.Type))
                        resultType = rhs.Type;
                    else
                        messages.Add(Message.TypeMismatch, branch.Right, resultType, rhs.Type);

                    rhs?.Free();
                }

                if (defaultBranch != null)
                {
                    var rhs = defaultBranch.Right.Accept(this);
                    
                    CurrentFunction.Body.Emit(Opcode.Move, c.Operand, rhs.Operand);

                    if (resultType == null || CobType.IsCastable(resultType, rhs.Type))
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
            var candidatesStorage = expression.FunctionExpression.Accept(this);

            // CAST
            var storage = VisitCastExpression(expression);

            // TUPLE ALLOCATION
            if (storage == null)
                storage = VisitTupleLiteralExpression(expression, candidatesStorage);

            // CALL
            if (storage == null)
                storage = VisitCallExpression(expression, candidatesStorage);

            candidatesStorage?.Free();

            return storage;
        }

        private Storage? VisitCastExpression(CallExpression expression)
        {
            if (expression.FunctionExpression is not IdentifierExpression ie)
                return null;
            
            if (!CobType.TryParse(ie.Value, out var castType)
            ||  castType.Type == eCobType.Tuple
            ||  castType.Type == eCobType.Struct
            ||  castType.Type == eCobType.None)
                return null;

            var arguments = expression.Arguments;
            if (arguments.Count != 1)
            {
                messages.Add(Message.FunctionParameterCountMismatch, expression, 1, arguments.Count);
                return null;
            }

            var source = arguments[0].Accept(this);
            var target = EmitCast(source, castType);

            return target;
        }

        private Storage? VisitTupleLiteralExpression(CallExpression expression, Storage? functionStorage)
        {
            if (functionStorage == null || functionStorage.Type != eCobType.Tuple
            ||  functionStorage.Type.Tag is not TupleType tupleType)
                return null;

            functionStorage.Free();

            var cobType = new CobType(eCobType.Tuple, tag: tupleType);

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
                    tagSignature.ReturnType
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
                messages.Add(Message.NoMatchingFunctionCandidate, expression.FunctionExpression, candidatesStorage?.Type.Tag?.ToString());
                return null;
            }

            // RECTIFY ARGUMENTS
            if (function.CallingConvention == CallingConvention.ThisCall)
            {
                Storage? thisStorage;
                if (expression.FunctionExpression is BinaryOperatorExpression { Operator: TokenType.Dot } boe)
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
                    var tag = Intrinsics.Array;
                    tag = tag.FindConcretizedStruct(bType) ?? tag.AllocateConcretizedStruct(bType);
                    var cobType = new CobType(eCobType.Struct, tag: tag);

                    if (tag.PopulateConcretizedStructIfRequired())
                        concreteTypeAstReferences.Add(new ConcreteTypeAstReference(Intrinsics.Array, tag));

                    // ALLOCATE
                    var arrReturnStorage = CurrentFunction.AllocateRegisterStorage(CobType.Nil);
                    argument = CurrentFunction.AllocateRegisterStorage(cobType);
            
                    // CALL CTOR
                    var arrNewFunction = tag.FindFactory("New");
                    var arrFunctionStorage = tag.EmitGetForSymbol(arrNewFunction);

                    CurrentFunction.Body.Emit(
                        Opcode.Call,
                        arrFunctionStorage.Operand,
                        argument.Operand,
                        new []{ Operand.ImmediateUnsigned(0) } // TODO Prealloc element count
                    );

                    // ADD ELEMENTS
                    arrNewFunction = tag.FindFunction("Add");
                    arrFunctionStorage = tag.EmitGetForSymbol(arrNewFunction);

                    for (; i < arguments.Count; ++i)
                    {
                        CurrentFunction.Body.Emit(
                            Opcode.Call,
                            arrFunctionStorage.Operand,
                            arrReturnStorage.Operand,
                            new []{ argument.Operand, arguments[i].Operand }
                        );
                    }

                    arrReturnStorage.Free();
                    arrFunctionStorage.Free();

                    while (j < arguments.Count)
                        arguments.RemoveAt(j);
                }

                // Casting
                if (argument.Type != parameter.Type)
                    argument = EmitCast(argument, parameter.Type);

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

            CurrentFunction.Body.Emit(
                function.Parent is TraitType ? Opcode.CallVirt : Opcode.Call,
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

            foreach (var scope in contextStack)
            {
                var target = scope.FindSymbol(expression.Value);
                if (target == null)
                    continue;

                if (!target.IsVisibleTo(CurrentFunction))
                    messages.Add(Message.CannotAccessPrivateSymbol, expression, expression.Value, CurrentFunction.Parent.Name);
                else if (inAssignment && target is Variable variable && !variable.Mutable)
                    messages.Add(Message.IllegalAssignmentImmutable, expression);

                if (inAssignment)
                {
                    var result = scope.EmitSetForSymbol(target);
                    if (!result)
                        messages.Add(Message.IllegalAssignment, expression);

                    return null;
                }
                else
                {
                    var result = scope.EmitGetForSymbol(target);
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
                CobType.None
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
                if (source.Type == eCobType.Struct && source.Type.Tag is StructType structType
                                                   && structType.Indexer != null)
                {
                    function = structType.Indexer.Setter;
                    context = structType;
                    operandArguments = new []{ source.Operand, index.Operand, AssignmentRHS.Operand };
                }
                else if (source.Type == eCobType.Tuple && source.Type.Tag is TupleType tupleType
                                                  && tupleType.Indexer != null)
                {
                    function = tupleType.Indexer.Setter;
                    context = tupleType;
                    operandArguments = new []{ source.Operand, index.Operand, AssignmentRHS.Operand };
                }
            }
            else // GET
            {
                if (source.Type == eCobType.Struct && source.Type.Tag is StructType structType
                                                   && structType.Indexer != null)
                {
                    function = index.Type.Tag == Intrinsics.Range
                             ? structType.FindFunction("Slice")
                             : structType.Indexer.Getter;
                    context = structType;
                    operandArguments = new []{ source.Operand, index.Operand };
                }
                else if (source.Type == eCobType.Tuple && source.Type.Tag is TupleType tupleType
                                                       && tupleType.Indexer != null)
                {
                    function = tupleType.Indexer.Getter;
                    context = tupleType;
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

        public Storage? Visit(StructLiteralExpression expression)
        {
            var structTypeStorage = expression.StructTypeExpression.Accept(this);
            if (structTypeStorage == null || structTypeStorage.Type != eCobType.Struct
            ||  structTypeStorage.Type.Tag is not StructType structType)
            {
                messages.Add(Message.CannotInstantiateType, expression.StructTypeExpression, structTypeStorage?.Type.ToString() ?? "(null)");
                return null;
            }

            var cobType = new CobType(eCobType.Struct, tag: structType);

            var storage = CurrentFunction.AllocateRegisterStorage(cobType);
            CurrentFunction.Body.Emit(Opcode.New, storage.Operand, cobType.ToOperand(artifact));

            if (expression.Assignments != null)
            {
                BinOpLHS = storage;
                contextStack.Push(structType);
                for (var i = 0; i < expression.Assignments.Count; ++i)
                    expression.Assignments[i].Accept(this);
                contextStack.Pop();
                BinOpLHS = null;
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
            var bType = CobType.U8; // TODO Implement correctly
            var tag = Intrinsics.Array;
            tag = tag.FindConcretizedStruct(bType) ?? tag.AllocateConcretizedStruct(bType);
            var cobType = new CobType(eCobType.Struct, tag: tag);

            if (tag.PopulateConcretizedStructIfRequired())
                concreteTypeAstReferences.Add(new ConcreteTypeAstReference(Intrinsics.Array, tag));

            var storage = CurrentFunction.AllocateRegisterStorage(cobType);

            // ALLOCATE & ADD ELEMENTS
            if (elements.Count == 2 && elements[0].IsSpread && elements[0].Storage?.Type == CobType.Int)
            {
                // Fill Elements
                // TODO Validate types match

                function = tag.FindFactory("New");
                functionStorage = tag.EmitGetForSymbol(function);

                CurrentFunction.Body.Emit(
                    Opcode.Call,
                    functionStorage.Operand,
                    storage.Operand,
                    new []{ elements[0].Storage.Operand }
                );
                functionStorage.Free();

                var countStorage = CurrentFunction.AllocateRegisterStorage(CobType.Int);
                var resultStorage = CurrentFunction.AllocateRegisterStorage(CobType.Boolean);
                var loopLabel = CurrentFunction.Body.AllocateLabel();
                var endLabel = CurrentFunction.Body.AllocateLabel();

                CurrentFunction.Body.Emit(Opcode.Move, countStorage.Operand, elements[0].Storage!.Operand);
                loopLabel.Mark();
                CurrentFunction.Body.Emit(Opcode.CmpLT, resultStorage.Operand, countStorage.Operand, Operand.ImmediateUnsigned(0));
                CurrentFunction.Body.Emit(Opcode.JmpT, resultStorage.Operand, endLabel);

                function = tag.FindFunction("Add");
                functionStorage = tag.EmitGetForSymbol(function);

                CurrentFunction.Body.Emit(
                    Opcode.Call,
                    functionStorage.Operand,
                    Operand.None,
                    new[] { storage.Operand, elements[1].Storage.Operand }
                );

                CurrentFunction.Body.Emit(Opcode.Sub, countStorage.Operand, countStorage.Operand, Operand.ImmediateUnsigned(1));
                CurrentFunction.Body.Emit(Opcode.Jmp, loopLabel);
                endLabel.Mark();

                functionStorage.Free();
                resultStorage.Free();
                countStorage.Free();
                elements[0].Storage.Free();
                elements[1].Storage.Free();
            }
            else
            {
                // Explicit Elements
                // TODO Validate types match

                function = tag.FindFactory("New");
                functionStorage = tag.EmitGetForSymbol(function);

                CurrentFunction.Body.Emit(
                    Opcode.Call,
                    functionStorage.Operand,
                    storage.Operand,
                    new []{ Operand.ImmediateUnsigned(elements.Count) }
                );
                functionStorage.Free();

                foreach (var element in elements)
                {
                    if (element.IsSpread)
                    {
                        function = tag.FindFunction("AddRange");
                        functionStorage = tag.EmitGetForSymbol(function);
                    }
                    else
                    {
                        function = tag.FindFunction("Add");
                        functionStorage = tag.EmitGetForSymbol(function);
                    }

                    CurrentFunction.Body.Emit(
                        Opcode.Call,
                        functionStorage.Operand,
                        Operand.None,
                        new[] { storage.Operand, element.Storage.Operand }
                    );

                    functionStorage.Free();
                    element.Storage.Free();
                }
            }

            return storage;
        }

        public Storage? Visit(NumberLiteralExpression expression)
        {
            return new Storage(
                new CobType(expression.Type, expression.BitSize),
                Operand.ImmediateSigned(expression.LongValue)
            );
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
            var byteCount = Encoding.UTF8.GetByteCount(expression.Value);
            var data = new byte[byteCount + 1];
            Encoding.UTF8.GetBytes(expression.Value, 0, expression.Value.Length, data, 0);

            var cobType = new CobType(eCobType.Tuple, tag: Intrinsics.Lens.FindConcretizedTuple(CobType.U8));
            var global = CurrentModule.AllocateGlobal($"string{Globals.Count}", cobType, false);
            global.StructValue = new[]
            {
                new Variable("Address", CobType.UInt, false, data),
                new Variable("Length", CobType.UInt, false, byteCount),
            };

            var idx = Globals.IndexOf(global);
            
            return new Storage(
                cobType,
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

        private static Storage EmitCast(Storage source, CobType dstType)
        {
            var srcType = source.Type;
            if (srcType == dstType)
                return source;
            
            if (srcType == eCobType.Unsigned
                ||  srcType == eCobType.Signed
                ||  srcType == eCobType.Float)
            {
                return source;
                // TODO Reimplement
                //return source with { Type = dstType };
            }
            else if (srcType == eCobType.Struct && dstType == eCobType.Trait)
            {
                return source; // TODO ??
            }
            else if (srcType == eCobType.Function && dstType == eCobType.Function)
            {
                return source; // TODO ??
            }
            else
                throw new NotImplementedException();
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
                for (var i = 0; i < compiler.Scripts.Count; ++i)
                    compiler.Scripts[i].Accept(compiler);
                
                ast.Accept(compiler);

                // HACK Really shouldn't be needed here
                foreach (var module in artifact.Modules)
                    module.InitializerFunction.Body.Emit(Opcode.Return);

                
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
                            TupleDeclStatement x => new TupleDeclStatement(x, concreteType.Concrete.Name),
                            StructDeclStatement x => new StructDeclStatement(x, concreteType.Concrete.Name),
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
    }

    internal interface IScopeContext
    {
        string Name { get; }

        IScopeContext? Parent { get; }

        ISymbol? FindSymbol(string name);

        Storage? EmitGetForSymbol(ISymbol symbol);

        bool EmitSetForSymbol(ISymbol symbol);
    }

    internal interface ISymbol
    {
        bool IsVisibleTo(IScopeContext context);
    }

    internal sealed record LoopContext(string? Tag, Label Continue, Label Break);
}
