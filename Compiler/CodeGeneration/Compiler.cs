using Compiler.Ast;
using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Ast.Visitors;
using Compiler.Interpreter;
using Compiler.Lexer;
using System.Diagnostics;
using System.Text;
using Compiler.CodeGeneration.Artifacts;

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
            Errors.AllocateGlobal("$errorType", CobType.Error, false);

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

            CurrentModule.InitializerFunction.Body.Emit(Opcode.Return);

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
            contextStack.Push(tupleType);
            foreach (var functionExpression in expression.Functions)
                functionExpression.Accept(this);
            contextStack.Pop();

            return null;
        }

        public Storage? Visit(StructDeclStatement expression)
        {
            var structType = CurrentModule.FindStructType(expression.Name)!;
            contextStack.Push(structType);
            foreach (var functionExpression in expression.Functions)
                functionExpression.Accept(this);
            contextStack.Pop();

            return null;
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
                function = CurrentModule.FindFunction(expression.Name)!;

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
                //var varIndex = CurrentFunction.AllocateStorage(CobType.Int);
                var index = boe.Left.Accept(this);
                var range = boe.Right.Accept(this);

                // TODO Should be abstracted behind an Intrinsics class
                // TODO Should use the Trait `Enumerable` instead of assuming the enumerator type
                var tdeRangeEnumerator = RootModule.FindTupleType("RangeEnumerator");
                var typeRangeTuple = new CobType(eCobType.Struct, tag: tdeRangeEnumerator);
                var varEnumerator = CurrentFunction.AllocateRegisterStorage(typeRangeTuple);

                // TODO tdeRangeEnumerator.FindFunctionIndex("GetEnumerator");
                CurrentFunction.Body.Emit(
                    Opcode.Call,
                    Operand.Function(Functions.FindIndex(x => x.Name == "GetEnumerator")),
                    varEnumerator.Operand,
                    new[] { range.Operand }
                );
                range.Free();

                startLabel.Mark();

                // TODO tdeRangeEnumerator.FindFunctionIndex("MoveNext");
                var moveNextResultStorage = CurrentFunction.AllocateRegisterStorage(CobType.Boolean);
                CurrentFunction.Body.Emit(
                    Opcode.Call,
                    Operand.Function(Functions.FindIndex(x => x.Name == "MoveNext")),
                    moveNextResultStorage.Operand,
                    new[] { varEnumerator.Operand }
                );

                CurrentFunction.Body.Emit(Opcode.JmpF, moveNextResultStorage.Operand, endLabel);
                CurrentFunction.Body.Emit(Opcode.GetField, index.Operand, varEnumerator.Operand, Operand.ImmediateUnsigned(0));
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

        public Storage? Visit(BlockExpression expression)
        {
            for (var i = 0; i < expression.Expressions.Count; i++)
            {
                var expr = expression.Expressions[i];
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

                var aType = lhs?.Type;
                var bType = CobType.FromString(rhs.Value);
                CobType cType;

                if (aType?.Tag is TupleType tupleType)
                {
                    cType = tupleType.FindOrAllocateConcretizedTupleType(bType);
                }
                else
                {
                    messages.Add(Message.IllegalTypeName, expression);
                    return null;
                }

                lhs?.Free();

                return new Storage(cType, Operand.None);
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
                var errorType = Errors.FindGlobal("$errorType");
                var error = Errors.EmitGetForSymbol(errorType);

                var labelEnd = CurrentFunction.Body.AllocateLabel();
                CurrentFunction.Body.Emit(Opcode.CmpTyEQ, c.Operand, lhs.Operand, error.Operand);
                CurrentFunction.Body.Emit(Opcode.JmpF, c.Operand, labelEnd);
                CurrentFunction.Body.Emit(Opcode.Move, lhs.Operand, rhs.Operand);
                labelEnd.Mark();

                c.Free();
                error?.Free();
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

                if (lhs != null) contextStack.Push((IScopeContext)lhs.Type.Tag);
                var rhs = expression.Right.Accept(this);
                if (lhs != null) contextStack.Pop();
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

                if (expression.Left is EmptyExpression)
                    a = new Storage(b.Type, Operand.ImmediateUnsigned(0));
                else if (expression.Right is EmptyExpression)
                    b = new Storage(a.Type, Operand.ImmediateUnsigned(~0));

                if (expression.Operator == TokenType.Range)
                {
                    var d = CurrentFunction.AllocateRegisterStorage(b.Type);
                    CurrentFunction.Body.Emit(Opcode.Sub, d.Operand, b.Operand, Operand.ImmediateUnsigned(1));
                    b.Free();
                    b = d;
                }
                else if (expression.Operator == TokenType.RangeLength)
                {
                    var d = CurrentFunction.AllocateRegisterStorage(b.Type);
                    CurrentFunction.Body.Emit(Opcode.Add, d.Operand, a.Operand, b.Operand);
                    b.Free();
                    b = d;
                }
                else if (expression.Operator == TokenType.RangeTerminal)
                {
                    var d = CurrentFunction.AllocateRegisterStorage(b.Type);
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
            var right = expression.Right.Accept(this);
            var c = CurrentFunction.AllocateRegisterStorage(right.Type);

            // TODO Support rhs logical !
            if (expression.Operator == TokenType.Not && conditionalStack != 0) // Logical !
            {
                CurrentFunction.Body.Emit(Opcode.Not, c.Operand, right.Operand);
            }
            else
            {
                var opcode = expression.Operator switch
                {
                    TokenType.Not => Opcode.BitNot,
                    TokenType.Subtract => Opcode.Neg,
                    _ => throw new ArgumentOutOfRangeException(nameof(expression))
                };

                CurrentFunction.Body.Emit(opcode, c.Operand, right.Operand);
            }

            right.Free();

            return c;
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
                var errorType = Errors.FindGlobal("$errorType");
                var error = Errors.EmitGetForSymbol(errorType);

                var labelEnd = CurrentFunction.Body.AllocateLabel();
                CurrentFunction.Body.Emit(Opcode.CmpTyEQ, c.Operand, lhs.Operand, error.Operand);
                CurrentFunction.Body.Emit(Opcode.JmpF, c.Operand, labelEnd);
                CurrentFunction.Body.Emit(Opcode.Return, lhs.Operand);
                labelEnd.Mark();

                c.Free();
                error.Free();

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

                CurrentFunction.Body.Emit(Opcode.CmpTyEQ, c.Operand, lhs.Operand, type.ToOperand(artifact));

                lhs.Free();

                return c;
            }
            else if (expression.RightMulti != null)
            {
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
                                               or StringLiteralExpression)
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
                                                    or StringLiteralExpression)
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
            // CAST OPERATOR
            var castType = VisitCastExpression(expression);
            if (castType != null)
                return castType;

            // FUNCTION IDENTIFIER
            var functionStorage = expression.FunctionExpression.Accept(this);
            
            // TUPLE ALLOCATION OPERATOR
            var tuple = VisitTupleLiteralExpression(expression, functionStorage);
            if (tuple != null)
                return tuple;

            var function = functionStorage?.Type.TagFunction;
            if (function == null)
            {
                messages.Add(Message.CannotCallType, expression, functionStorage?.Type.ToString() ?? "(null)");
                return null;
            }

            var retStorage = function.ReturnType != eCobType.None
                           ? CurrentFunction.AllocateRegisterStorage(function.ReturnType)
                           : null;

            // ARGUMENTS
            var parameters = function.Parameters;
            var arguments = expression.Arguments;
            if (function.CallingConvention == CallingConvention.ThisCall)
            {
                var lArguments = new List<Expression>();
                lArguments.Add(new IdentifierExpression(new Token(TokenType.Identifier, "$this", "", 0, 0)));
                lArguments.AddRange(arguments);
                arguments = lArguments;
            }
            var hasSpreadParameter = parameters.Count > 0 && parameters[^1].IsSpread;
            if (arguments.Count != parameters.Count && !hasSpreadParameter)
                messages.Add(Message.FunctionParameterCountMismatch, expression, parameters.Count, arguments.Count);
            
            IReadOnlyList<Operand>? operandArguments;
            if (arguments.Count > 0)
            {
                var aOperandArguments = new Operand[arguments.Count];
                for (int i = 0; i < arguments.Count; ++i)
                {
                    var paramType = parameters.ElementAtOrDefault(i);
                    var argument = arguments[i];
                    Storage? argStorage;
                    if (argument is IdentifierExpression argIdent && argIdent.Value == "$this")
                    {
                        var boe = (BinaryOperatorExpression)expression.FunctionExpression;
                        argStorage = boe.Left.Accept(this);
                    }
                    else
                        argStorage = argument.Accept(this);

                    if (paramType != null && paramType.IsSpread) paramType = null;
                    if (argStorage == null
                    ||  (paramType != null && !CobType.IsCastable(argStorage.Type, paramType.Type)))
                    {
                        aOperandArguments[i] = Operand.None;
                        messages.Add(Message.TypeMismatch, arguments[i], paramType?.Type, argStorage.Type);
                        continue;
                    }
                    
                    if (paramType != null && argStorage.Type != paramType.Type)
                        argStorage = EmitCast(argStorage, paramType.Type);

                    aOperandArguments[i] = argStorage.Operand;
                }

                operandArguments = aOperandArguments;
            }
            else
                operandArguments = null;
            
            // CALL
            var opcode = function.Parent is TraitType ? Opcode.CallVirt : Opcode.Call;
            CurrentFunction.Body.Emit(opcode, functionStorage.Operand, retStorage?.Operand, operandArguments);

            if (function.ReturnType.HasErrorFlag && conditionalStack <= 0)
            {
                CurrentFunction.Body.Emit(Opcode.PanicOnErr, retStorage.Operand);
            }

            // CLEANUP
            functionStorage.Free(); // function reg
            if (operandArguments != null)
            {
                for (int i = 0; i < operandArguments.Count; ++i) // argument regs
                    CurrentFunction.FreeStorage(operandArguments[i]);
            }

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

        private Storage EmitCast(Storage source, CobType dstType)
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
            else
                throw new NotImplementedException();
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
            var target = expression.Expression.Accept(this);

            var storage = CurrentFunction.AllocateRegisterStorage(new CobType(eCobType.Lens, elementType: expression.ElementType));
            CurrentFunction.Body.Emit(Opcode.Lens, storage.Operand, target.Operand);
            target.Free();

            return storage;
        }

        public Storage? Visit(IndexerExpression expression)
        {
            Storage? storage = null;

            var source = expression.Left.Accept(this);
            var index = expression.Index.Accept(this);

            if (source == null)
                messages.Add(Message.CannotIndexType, expression, "none");
            else if (source.Type == eCobType.Struct && source.Type.Tag is StructType structType
            &&  structType.Indexer != null)
            {
                structType.Indexer.Index = index;
                BinOpLHS = source; // ???
                contextStack.Push(structType.Indexer);
                storage = structType.Indexer.GetterExpression.Accept(this);
                contextStack.Pop();
                BinOpLHS = null;
                structType.Indexer.Index = null;
            }
            else if (source.Type == eCobType.Lens)
            {
                storage = CurrentFunction.AllocateRegisterStorage(source.Type.ElementType);
                CurrentFunction.Body.Emit(
                    Opcode.GetElem,
                    storage.Operand,
                    source.Operand,
                    index.Operand
                );
            }
            else
                messages.Add(Message.CannotIndexType, expression, source.Type);

            index?.Free();
            source?.Free();

            return storage;
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

            var global = CurrentModule.AllocateGlobal($"string{Globals.Count}", CobType.String, false);
            global.BufferValue = data;

            var idx = Globals.IndexOf(global);
            
            return new Storage(
                CobType.String,
                Operand.Global(idx)
            );
        }

        public Storage? Visit(EmptyExpression expression)
        {
            return null;
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
                var pass0 = new ForwardDeclaration(compiler, messages);
                ast.Accept(pass0);


                // PASS 1 - Compilation
                // NOTE We must compile all imported scripts before we compile this script; list is in import order
                Intrinsics.InitializeForPass1(compiler);
                for (var i = 0; i < compiler.Scripts.Count; ++i)
                    compiler.Scripts[i].Accept(compiler);
                
                ast.Accept(compiler);
                
                return artifact;
            }
            finally
            {
                stopwatch.Stop();
            }
        }

        public static long TotalMilliseconds => stopwatch.ElapsedMilliseconds;
        private static readonly Stopwatch stopwatch = new ();
    }

    internal sealed record Import
    {
        public string Library { get; init; }

        public string? SymbolName { get; init; }

        public Function? Function { get; init; }
    }

    internal sealed record Export
    {
        public Function Function { get; init; }
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
