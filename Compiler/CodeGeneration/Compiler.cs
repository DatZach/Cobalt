using Compiler.Ast;
using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Ast.Visitors;
using Compiler.Interpreter;
using Compiler.Lexer;
using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

namespace Compiler.CodeGeneration
{
    internal sealed class Compiler : IExpressionVisitor<Storage?>
    {
        // TODO "Artifact" class which contains the resulting compiled code

        public List<ArtifactStatement> Artifacts { get; }

        public List<Import> Imports { get; }

        public List<Export> Exports { get; }

        public List<Module> Modules { get; }

        public List<CobVariable> Globals { get; }

        public List<Function> Functions { get; }

        public List<ScriptExpression> Scripts { get; }

        public Function? EntryFunction { get; private set; }

        // TODO Possible to collapse contextStack and functionStack into 1?
        public Function? CurrentFunction => functionStack.Count == 0 ? null : functionStack.Peek();

        public IContext ParentContext => contextStack.Skip(1).FirstOrDefault(); // TODO Remove

        public IContext CurrentContext => contextStack.Peek();

        public Module RootModule { get; }

        public Module CurrentModule { get; private set; } // TODO Make this private again

        public MessageCollection Messages { get; } // TODO Make this private again, somehow

        private readonly Stack<Function> functionStack;
        private readonly Stack<IContext> contextStack; // TODO IScopedContext or something (Struct, Module, Tuple, etc.)
        private readonly Stack<LoopContext> loopStack;

        private Compiler(MessageCollection messages)
        {
            Artifacts = new List<ArtifactStatement>();
            Imports = new List<Import>();
            Exports = new List<Export>();
            Modules = new List<Module>();
            Globals = new List<CobVariable>();
            Scripts = new List<ScriptExpression>();
            Functions = new List<Function>();
            functionStack = new Stack<Function>();
            contextStack = new Stack<IContext>();
            loopStack = new Stack<LoopContext>();

            RootModule = new Module(this, null, null);
            Modules.Add(RootModule);

            this.Messages = messages ?? throw new ArgumentNullException(nameof(messages));
        }

        public Storage? Visit(ScriptExpression expression)
        {
            CurrentModule = RootModule;

            contextStack.Push(RootModule);
            functionStack.Push(CurrentModule.InitializerFunction);

            var expressions = expression.Expressions;
            for (int i = 0; i < expressions.Count; ++i)
                expressions[i].Accept(this);

            CurrentModule.InitializerFunction.Body.Emit(Opcode.Return);

            // TODO Needs to be unified with the copy-pasted code in ModuleExpression
            contextStack.Pop();
            CurrentModule = contextStack.Count == 0 ? RootModule : contextStack.Peek() as Module ?? RootModule;
            
            functionStack.Pop();

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

            var prevModule = CurrentModule;
            CurrentModule = module;
            contextStack.Push(module);

            functionStack.Push(module.InitializerFunction);

            if (expression.Block != null)
            {
                var storage = expression.Block.Accept(this);
                storage?.Free();

                // TODO Needs to be unified with the copy-pasted code in ScriptExpression
                contextStack.Pop();
                CurrentModule = prevModule;

                functionStack.Pop();

                module.InitializerFunction.Body.Emit(Opcode.Return);
            }

            return null;
        }

        public Storage? Visit(TypeAliasStatement expression)
        {
            return null;
        }

        public Storage? Visit(TupleDeclStatement expression)
        {
            // TODO CurrentContext?
            //      Is it possible to get rid of CurrentModule entirely?
            var tupleType = CurrentModule.FindTupleType(expression.Name)!;
            contextStack.Push(tupleType);
            foreach (var functionExpression in expression.Functions)
                functionExpression.Accept(this);
            contextStack.Pop();

            return null;
        }

        public Storage? Visit(StructDeclStatement expression)
        {
            // TODO CurrentContext?
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
                Messages.Add(Message.MissingFunctionBody, expression);
                return null;
            }

            // TODO Make a clean API for this
            Function function;
            if (CurrentContext is StructType structType)
                function = structType.Functions.First(x => x.Name == expression.Name);
            else if (CurrentContext is TupleType tupleType)
                function = tupleType.Functions.First(x => x.Name == expression.Name);
            else
                function = CurrentModule.Functions.First(x => x.Name == expression.Name);

            functionStack.Push(function);
            contextStack.Push(function);

            // TODO Temporary hack to call our initializer functions in the entry point
            if (HACK_isDivingFatArrow)
            {
                foreach (var module in Modules)
                {
                    CurrentFunction.Body.Emit(
                        Opcode.Call,
                        new Operand { Type = OperandType.Function, Value = Functions.FindIndex(x => x.FullyQualifiedName == module.InitializerFunction.FullyQualifiedName) },
                        Operand.R0,
                        Array.Empty<Operand>()
                    );
                }
            }

            expression.Body.Accept(this);
            
            function.ReturnLabel.Mark();
            function.Body.Emit(Opcode.Return); // TODO Error if not all paths return

            contextStack.Pop();
            functionStack.Pop();

            //function.Body.HACK_Optmize();

            return new Storage(
                CurrentFunction,
                new Operand(), //null, // TODO ???
                new CobType(eCobType.Function, tag: function)
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
                        Messages.Add(Message.TypeMismatch, expression, "any", "none");
                    else if (type != null && !CobType.IsCastable(rhs.Type, type))
                        Messages.Add(Message.TypeMismatch, expression, type, rhs.Type);
                    else
                        type = rhs.Type;
                }
                else
                    rhs = null;

                if ((decl.Type == null && decl.Initializer == null)
                ||  type == null)
                {
                    Messages.Add(Message.MalformedVarDeclaration, expression);
                    continue;
                }
                
                var variable = new CobVariable(decl.Name, type, mutable);

                if (CurrentFunction != null)
                {
                    if (CurrentFunction == CurrentModule.InitializerFunction) // Global Decl
                    {
                        var global = FindGlobal(CurrentModule.Variables[decl.Name]);
                        if (rhs != null)
                        {
                            CurrentFunction.Body.Emit( // TODO Not right
                                Opcode.Move,
                                new Operand { Type = OperandType.Global, Value = global, Size = type.Size },
                                rhs.Operand
                            ); // TODO EmitLO
                        }
                    }
                    else
                    {
                        var local = CurrentFunction.AllocateLocal(variable);
                        if (rhs != null)
                        {
                            CurrentFunction.Body.Emit(
                                Opcode.Move,
                                new Operand { Type = OperandType.Local, Value = local, Size = type.Size },
                                rhs.Operand
                            ); // TODO EmitLO
                        }
                    }
                }
                else
                    throw new NotImplementedException(); // TODO Normal compile error? What does this mean??

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
                Messages.Add(Message.TypeMismatch, expression.Conditional, CobType.Boolean, conditional?.Type.ToString() ?? "none");
                return null;
            }
            else if (expression.Conditional is IdentifierExpression)
            {
                var c = CurrentFunction.AllocateStorage(CobType.Boolean);
                CurrentFunction.Body.Emit(Opcode.CmpEQ, c.Operand, conditional.Operand, new Operand { Type = OperandType.ImmediateUnsigned, Value = 1 });
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

                // TODO Should not find types like this...
                var tdeRangeEnumerator = RootModule.TupleTypes.First(x => x.Name == "RangeEnumerator");
                var typeRangeTuple = new CobType(eCobType.Struct, tag: tdeRangeEnumerator);
                var varEnumerator = CurrentFunction.AllocateStorage(typeRangeTuple);

                // TODO Need to refactor TupleType to be a discrete class instead of the AST expression...
                // TODO tdeRangeEnumerator.FindFunctionIndex("GetEnumerator");
                CurrentFunction.Body.Emit(
                    Opcode.Call,
                    new Operand { Type = OperandType.Function, Value = Functions.FindIndex(x => x.Name == "GetEnumerator") },
                    varEnumerator.Operand,
                    new[] { range.Operand }
                );
                range.Free();

                startLabel.Mark();

                // TODO tdeRangeEnumerator.FindFunctionIndex("MoveNext");
                var moveNextResultStorage = CurrentFunction.AllocateStorage(CobType.Boolean);
                CurrentFunction.Body.Emit(
                    Opcode.Call,
                    new Operand { Type = OperandType.Function, Value = Functions.FindIndex(x => x.Name == "MoveNext") },
                    moveNextResultStorage.Operand,
                    new[] { varEnumerator.Operand }
                );

                CurrentFunction.Body.Emit(Opcode.JmpF, moveNextResultStorage.Operand, endLabel);
                CurrentFunction.Body.Emit(Opcode.GetField, index.Operand, varEnumerator.Operand, new Operand { Type = OperandType.ImmediateUnsigned, Value = 0 });
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
                Messages.Add(Message.CannotContinue, expression);
                return null;
            }

            CurrentFunction.Body.Emit(Opcode.Jmp, loopStack.Peek().Continue);

            return null;
        }

        public Storage? Visit(BreakStatement expression)
        {
            if (loopStack.Count == 0)
            {
                Messages.Add(Message.CannotBreak, expression);
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
                    Messages.Add(Message.CannotBreakNoLabel, expression, expression.Label.Value);
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
                else if (CurrentFunction.ReturnType != rhs.Type)
                    Messages.Add(Message.ReturnTypeMismatch, expression, CurrentFunction.ReturnType, rhs.Type);
                
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
                if (EntryFunction != null)
                    Messages.Add(Message.CannotRedeclareEntryPoint, expression);
                else
                {
                    EntryFunction = value.Type.TagFunction;
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

            if (expression.Operator == TokenType.Dot) // Dereference a.b
            {
                // TODO Clean this up
                var prevAsnSrc = AssignmentRHS;
                AssignmentRHS = null;
                var lhs = expression.Left.Accept(this);
                AssignmentRHS = prevAsnSrc;
                BinOpLHS = lhs;

                if (lhs != null) contextStack.Push((IContext)lhs.Type.Tag);
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
                var c = CurrentFunction.AllocateStorage(a.Type);

                int shlValue = b.Type.Size;

                CurrentFunction.Body.Emit(Opcode.BitShl, c.Operand, a.Operand, new Operand { Type = OperandType.ImmediateUnsigned, Value = shlValue });
                CurrentFunction.Body.Emit(Opcode.BitOr, c.Operand, a.Operand, b.Operand);
                
                b.Free();
                a.Free();

                return c;
            }
            else if (expression.Operator is TokenType.Range or TokenType.RangeInclusive
                                         or TokenType.RangeLength or TokenType.RangeTerminal)
            {
                // TODO Should be allocateable on a Register
                // TODO Should not discover this type like this...
                var tdeRangeTuple = RootModule.TupleTypes.First(x => x.Name == "Range");
                var typeRangeTuple = new CobType(eCobType.Tuple, tag: tdeRangeTuple);
                var local = CurrentFunction.AllocateLocal(new CobVariable("$tuple", typeRangeTuple, false)
                {
                    StructValue = tdeRangeTuple.Fields.Select(x => new CobVariable(x.Name, x.Type, true)).ToArray()
                });
                var c = CurrentFunction.AllocateStorage(typeRangeTuple, local);

                var a = expression.Left.Accept(this);
                var b = expression.Right.Accept(this);

                if (expression.Left is EmptyExpression)
                    a = CurrentFunction.AllocateStorage(b.Type, 0);
                else if (expression.Right is EmptyExpression)
                    b = CurrentFunction.AllocateStorage(a.Type, ~0);

                if (expression.Operator == TokenType.Range)
                {
                    var d = CurrentFunction.AllocateStorage(b.Type);
                    CurrentFunction.Body.Emit(Opcode.Sub, d.Operand, b.Operand, new Operand { Type = OperandType.ImmediateUnsigned, Value = 1 });
                    b.Free();
                    b = d;
                }
                else if (expression.Operator == TokenType.RangeLength)
                {
                    var d = CurrentFunction.AllocateStorage(b.Type);
                    CurrentFunction.Body.Emit(Opcode.Add, d.Operand, a.Operand, b.Operand);
                    b.Free();
                    b = d;
                }
                else if (expression.Operator == TokenType.RangeTerminal)
                {
                    var d = CurrentFunction.AllocateStorage(b.Type);
                    CurrentFunction.Body.Emit(Opcode.BitNot, d.Operand, b.Operand);
                    b.Free();
                    b = d;
                }

                CurrentFunction.Body.Emit(Opcode.SetField, c.Operand, new Operand { Type = OperandType.ImmediateUnsigned, Value = 0 }, a.Operand);
                CurrentFunction.Body.Emit(Opcode.SetField, c.Operand, new Operand { Type = OperandType.ImmediateUnsigned, Value = 1 }, b.Operand);

                b.Free();
                a.Free();

                return c;
            }
            else if (cndOpcode != Opcode.None) // Conditional && ||
            {
                var a = expression.Left.Accept(this);
                var b = expression.Right.Accept(this);
                var c = CurrentFunction.AllocateStorage(CobType.Boolean);

                CurrentFunction.Body.Emit(cndOpcode, c.Operand, a.Operand, b.Operand);

                b.Free();
                a.Free();

                return c;
            }
            else if (cmpOpcode != Opcode.None) // Equality == != < > <= >=
            {
                var a = expression.Left.Accept(this);
                var b = expression.Right.Accept(this);
                var c = CurrentFunction.AllocateStorage(CobType.Boolean);

                CurrentFunction.Body.Emit(cmpOpcode, c.Operand, a.Operand, b.Operand);

                b.Free();
                a.Free();

                return c;
            }
            else if (artOpcode != Opcode.None) // Arithmetic + - * /
            {
                var a = expression.Left.Accept(this);
                var b = expression.Right.Accept(this);
                var c = CurrentFunction.AllocateStorage(a.Type);
                
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
            var c = CurrentFunction.AllocateStorage(right.Type);

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
            throw new NotImplementedException();
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
                Messages.Add(Message.CannotCallType, expression, functionStorage?.Type.ToString() ?? "(null)");
                return null;
            }

            var retStorage = function.ReturnType != eCobType.None
                           ? CurrentFunction.AllocateStorage(function.ReturnType)
                           : null;

            // ARGUMENTS
            var parameters = function.Parameters;
            var arguments = expression.Arguments;
            if (function.CallingConvention == CallingConvention.ThisCall)
            {
                var lArguments = new List<Expression>(arguments);
                lArguments.Insert(0, new IdentifierExpression(new Token(TokenType.Identifier, "range", "", 0, 0)));
                arguments = lArguments;
            }
            var hasSpreadParameter = parameters.Count > 0 && parameters[^1].IsSpread;
            if (arguments.Count != parameters.Count && !hasSpreadParameter)
                Messages.Add(Message.FunctionParameterCountMismatch, expression, parameters.Count, arguments.Count);
            
            IReadOnlyList<Operand>? operandArguments;
            if (arguments.Count > 0)
            {
                var aOperandArguments = new Operand[arguments.Count];
                for (int i = 0; i < arguments.Count; ++i)
                {
                    var paramType = parameters.ElementAtOrDefault(i);
                    var argStorage = arguments[i].Accept(this);

                    if (paramType != null && paramType.IsSpread) paramType = null;
                    if (argStorage == null
                    ||  (paramType != null && !CobType.IsCastable(argStorage.Type, paramType.Type)))
                    {
                        aOperandArguments[i] = Operand.None;
                        Messages.Add(Message.TypeMismatch, arguments[i], paramType?.Type, argStorage.Type);
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
            CurrentFunction.Body.Emit(Opcode.Call, functionStorage.Operand, retStorage?.Operand, operandArguments);

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
                var target = scope.FindIdentifier(expression.Value);
                if (target == null)
                    continue;

                // TODO How to handle Module = 1234;?
                if (!target.IsVisibleTo(CurrentModule)) // TODO CurrentContext.Name...?
                    Messages.Add(Message.CannotAccessPrivateSymbol, expression, expression.Value, CurrentModule.Name ?? "(root)");
                else if (inAssignment && target is CobVariable variable && !variable.Mutable) // TODO Is this actually appropriate?
                    Messages.Add(Message.IllegalAssignmentImmutable, expression);

                if (inAssignment)
                {
                    var result = scope.EmitSetIdentifier(target);
                    if (!result)
                        Messages.Add(Message.IllegalAssignment, expression);

                    return null;
                }
                else
                {
                    var result = scope.EmitGetIdentifier(target);
                    if (result == null)
                        Messages.Add(Message.IllegalAssignment, expression);

                    return result;
                }
            }

            Messages.Add(Message.UndeclaredIdentifier, expression, expression.Value);
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
                Messages.Add(Message.FunctionParameterCountMismatch, expression, 1, arguments.Count);
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
                return source with { Type = dstType };
            }
            else
                throw new NotImplementedException();
        }

        public Storage? Visit(AheadOfTimeExpression expression)
        {
            var function = new Function(
                "$aot_eval$",
                this,
                CurrentModule,
                CallingConvention.CCall,
                Array.Empty<Function.Parameter>(),
                CobType.None
            );

            functionStack.Push(function);
            var evalStorage = expression.Expression.Accept(this);
            if (evalStorage == null || evalStorage.Type == eCobType.None)
                Messages.Add(Message.AotCannotUseVoid, expression);

            function.ReturnType = evalStorage.Type;
            CurrentFunction.Body.Emit(Opcode.Return, evalStorage.Operand);
            evalStorage.Free();

            function.ReturnLabel.Mark();
            
            functionStack.Pop();

            using var vm = new VirtualMachine(this);
            var result = vm.ExecuteFunction(function);

            var reg = CurrentFunction.AllocateStorage(function.ReturnType);
            // TODO EmitOI
            CurrentFunction.Body.Emit(
                Opcode.Move,
                reg.Operand,
                new Operand { Type = OperandType.ImmediateUnsigned, Size = -1, Value = result.IntValue } // TODO Not right
            );
            
            return evalStorage;
        }

        public Storage? Visit(LensExpression expression)
        {
            var target = expression.Expression.Accept(this);

            var storage = CurrentFunction.AllocateStorage(new CobType(eCobType.Lens, elementType: expression.ElementType));
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
                Messages.Add(Message.CannotIndexType, expression, "none");
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
                storage = CurrentFunction.AllocateStorage(source.Type.ElementType);
                CurrentFunction.Body.Emit(
                    Opcode.GetElem,
                    storage.Operand,
                    source.Operand,
                    index.Operand
                );
            }
            else
                Messages.Add(Message.CannotIndexType, expression, source.Type);

            index?.Free();
            source?.Free();

            return storage;
        }

        private Storage? VisitTupleLiteralExpression(CallExpression expression, Storage? functionStorage)
        {
            if (functionStorage == null || functionStorage.Type != eCobType.Tuple
            ||  functionStorage.Type.Tag is not TupleType tde)
                return null;

            functionStorage.Free();
            
            // TODO Should be allocateable on a Register
            // TODO Is this really the best way to allocate a tuple?
            var local = CurrentFunction.AllocateLocal(new CobVariable("$tuple", functionStorage.Type, false)
            {
                StructValue = tde.Fields.Select(x => x.DeepClone()).ToArray()
            });

            for (var i = 0; i < expression.Arguments.Count; ++i)
            {
                var argStorage = expression.Arguments[i].Accept(this);
                CurrentFunction.Body.Emit(
                    Opcode.SetField,
                    new Operand { Type = OperandType.Local, Value = local },
                    new Operand { Type = OperandType.ImmediateUnsigned, Value = i },
                    argStorage.Operand
                );
            }

            return CurrentFunction.AllocateStorage(functionStorage.Type, local);
        }

        public Storage? Visit(StructLiteralExpression expression)
        {
            var structTypeStorage = expression.StructTypeExpression.Accept(this);
            if (structTypeStorage == null || structTypeStorage.Type != eCobType.Struct
            ||  structTypeStorage.Type.Tag is not StructType sde)
            {
                Messages.Add(Message.CannotInstantiateType, expression.StructTypeExpression, structTypeStorage?.Type.ToString() ?? "(null)");
                return null;
            }
            
            var local = CurrentFunction.AllocateLocal(new CobVariable("$struct", structTypeStorage.Type, false)
            {
                StructValue = sde.Fields.Select(x => x.DeepClone()).ToArray()
            });

            var storage = CurrentFunction.AllocateStorage(structTypeStorage.Type, local);

            if (expression.Assignments != null)
            {
                BinOpLHS = storage;
                contextStack.Push(sde);
                for (var i = 0; i < expression.Assignments.Count; ++i)
                    expression.Assignments[i].Accept(this);
                contextStack.Pop();
                BinOpLHS = null;
            }

            return storage;
        }

        public Storage? Visit(NumberLiteralExpression expression)
        {
            return CurrentFunction.AllocateStorage(
                new CobType(expression.Type, expression.BitSize),
                expression.LongValue
            );
        }

        public Storage? Visit(BooleanLiteralExpression expression)
        {
            return CurrentFunction.AllocateStorage(CobType.Boolean, expression.Value ? 1 : 0);
        }

        public Storage? Visit(StringLiteralExpression expression)
        {
            var byteCount = Encoding.UTF8.GetByteCount(expression.Value);
            var data = new byte[byteCount + 1];
            Encoding.UTF8.GetBytes(expression.Value, 0, expression.Value.Length, data, 0);
            
            var global = AllocateGlobal(new CobVariable(
                $"string{Globals.Count}",
                CobType.String,
                false
            ) { BufferValue = data });
            
            // TODO AllocateStorage on Global "function"??
            return new Storage(
                CurrentFunction,
                new Operand
                {
                    Type = OperandType.Global,
                    Value = global,
                    Size = -1
                },
                CobType.String
            );
        }

        public Storage? Visit(EmptyExpression expression)
        {
            return null;
        }

        public int FindGlobal(CobVariable variable)
        {
            return Globals.FindIndex(x => x == variable);
        }
        
        public int AllocateGlobal(CobVariable variable)
        {
            if (variable == null) throw new ArgumentNullException(nameof(variable));

            if (FindGlobal(variable) != -1)
                return -1;

            var idx = Globals.Count;
            Globals.Add(variable);

            return idx;
        }

        public static bool StandardIsSymbolVisibleHeuristic(IContext? context, IContext? parent, string? name)
        {
            for (var ctx = context; ctx != null; ctx = ctx.Parent)
            {
                if (ctx == parent)
                    return true;
            }

            return !string.IsNullOrEmpty(name) && char.IsUpper(name[0]);
        }

        public static Compiler Compile(ScriptExpression ast, MessageCollection messages)
        {
            try
            {
                stopwatch.Start();
                
                var compiler = new Compiler(messages);

                // HACKish Probably fine to initialize intrinsics in an "odd" way, but this does need more abstraction
                StringContext.Instance.Compiler = compiler;
                
                var pass0 = new ForwardDeclaration(compiler);
                ast.Accept(pass0);

                // NOTE We must compile all imported scripts before we compile this script; list is in import order
                for (var i = 0; i < compiler.Scripts.Count; ++i)
                    compiler.Scripts[i].Accept(compiler);
                
                ast.Accept(compiler);
                
                return compiler;
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

    // TODO Abstract Class
    // TODO Should inherit ISymbol?
    internal interface IContext
    {
        string Name { get; }

        IContext? Parent { get; }

        ISymbol? FindIdentifier(string name);

        Storage? EmitGetIdentifier(ISymbol identifier);

        bool EmitSetIdentifier(ISymbol identifier);

        // TODO Remove compiler field? Most contexts already have the compiler passed in ctor
        //Storage? GetIdentifier(Compiler compiler, IdentifierExpression expression);

        //CobVariable? SetIdentifier(Compiler compiler, IdentifierExpression expression);
    }

    internal interface ISymbol
    {
        bool IsVisibleTo(IContext context);
    }

    internal sealed record LoopContext(string? Tag, Label Continue, Label Break);
}
