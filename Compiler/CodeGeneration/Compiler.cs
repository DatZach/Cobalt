using System.Text;
using Compiler.Ast;
using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Ast.Visitors;
using Compiler.Interpreter;
using Compiler.Lexer;

namespace Compiler.CodeGeneration
{
    internal sealed class Compiler : IExpressionVisitor<Storage?> // TODO Nullable?
    {
        public List<ArtifactExpression> Artifacts { get; }

        public List<Import> Imports { get; }

        public List<Module> Modules { get; }

        public Dictionary<string, string> Exports { get; } // (ExternalName, InternalName)

        // TODO Optimization
        //public Dictionary<string, CobVariable> Globals { get; }
        public List<CobVariable> Globals { get; }

        public Function? EntryFunction { get; private set; }

        private Function? CurrentFunction => functionStack.Count == 0 ? null : functionStack.Peek();

        private Module CurrentContext => contextStack.Peek();

        private Module CurrentModule { get; set; }

        private readonly Module rootModule;
        private readonly Stack<Function> functionStack;
        private readonly Stack<Module> contextStack; // TODO IScopedContext or something (Struct, Module, Tuple, etc.)
        private readonly MessageCollection messages;
        
        public Compiler(MessageCollection messages)
        {
            Artifacts = new List<ArtifactExpression>();
            Imports = new List<Import>();
            Exports = new Dictionary<string, string>();
            Modules = new List<Module>();
            Globals = new List<CobVariable>();
            functionStack = new Stack<Function>();
            contextStack = new Stack<Module>();

            CurrentModule = rootModule = new Module();
            Modules.Add(rootModule);
            contextStack.Push(rootModule);

            this.messages = messages ?? throw new ArgumentNullException(nameof(messages));
        }

        public Storage? Visit(ScriptExpression expression)
        {
            var expressions = expression.Expressions;
            for (int i = 0; i < expressions.Count; ++i)
                expressions[i].Accept(this);

            return null;
        }

        public Storage? Visit(ModuleExpression expression)
        {
            var module = Modules.FirstOrDefault(x => x.Name == expression.Name);
            if (module == null)
            {
                module = new Module { Name = expression.Name };
                Modules.Add(module);
            }

            if (CurrentModule != rootModule && module != rootModule)
            {
                messages.Add(Message.CannotNestModules, expression);
                return null;
            }

            CurrentModule = module;
            contextStack.Push(module);

            if (expression.Block != null)
            {
                var storage = expression.Block.Accept(this);
                storage?.Free();

                contextStack.Pop();
                CurrentModule = rootModule;
            }

            return null;
        }

        public Storage? Visit(VarExpression expression)
        {
            for (var i = 0; i < expression.Declarations.Count; ++i)
            {
                var decl = expression.Declarations[i];

                // TODO x = Module
                // TODO var a; illegal
                // TODO var a: u16;
                // TODO Throw error when initializer returns something unassignable
                var rhs = decl.Initializer?.Accept(this);
                if (rhs == null)
                    continue;

                var mutable = expression.Type == TokenType.Var;
                var variable = new CobVariable(decl.Name, rhs.Type, mutable);

                if (CurrentFunction != null) // Local Decl
                {
                    var local = CurrentFunction.AllocateLocal(variable);
                    CurrentFunction.Body.EmitOO(
                        Opcode.Move,
                        new Operand { Type = OperandType.Local, Value = local, Size = rhs.Type.Size },
                        rhs.Operand
                    ); // TODO EmitLO
                }
                else if (CurrentFunction == null) // Global Decl
                {
                    AllocateGlobal(variable);

                    // TODO Throw exception if export declared outside root level
                    // TODO Throw exception if export declared on non-function?
                    if (expression.Type == TokenType.Export)
                        Exports.Add(decl.Name, rhs.Type.Function.Name);
                }

                rhs.Free();
            }

            return null;
        }

        public Storage? Visit(ImportExpression expression)
        {
            if (expression.SymbolName == null)
            {
                // TODO Library import
            }
            else
            {
                // Symbol import
                Function? function;
                if (expression.FunctionSignture != null)
                {
                    function = new Function(
                        expression.SymbolName,
                        CurrentModule,
                        expression.FunctionSignture.CallingConvention,
                        expression.FunctionSignture.Parameters,
                        new CobType(eCobType.None, 0)
                    );
                }
                else
                    function = null;

                var import = new Import
                {
                    Library = expression.Library,
                    SymbolName = expression.SymbolName,
                    Function = function
                };

                Imports.Add(import);
                
                if (function != null)
                {
                    function.NativeImport = import;
                    AllocateGlobal(new CobVariable(
                        expression.SymbolName,
                        new CobType(eCobType.Function, -1, function: function),
                        false
                    ));
                }
            }

            return null;
        }

        public Storage? Visit(ArtifactExpression expression)
        {
            Artifacts.Add(expression);
            return null;
        }

        public Storage? Visit(IfStatement expression)
        {
            var elseLabel = CurrentFunction.Body.AllocateLabel();
            var endLabel = CurrentFunction.Body.AllocateLabel();

            var conditional = expression.Conditional.Accept(this);
            CurrentFunction.Body.EmitOO(
                Opcode.Compare,
                conditional.Operand,
                new Operand { Type = OperandType.ImmediateUnsigned, Size = 32, Value = 0 }
            );
            CurrentFunction.Body.EmitL(Opcode.JumpIfFalse, elseLabel);
            conditional.Free();

            expression.Then.Accept(this)?.Free();
            CurrentFunction.Body.EmitL(Opcode.Jump, endLabel);

            elseLabel.Mark();

            expression.Else?.Accept(this);

            endLabel.Mark();

            return null;
        }

        public Storage? Visit(ReturnStatement expression)
        {
            if (expression.Expression == null)
                CurrentFunction.Body.Emit(Opcode.Return);
            else
            {
                var rhs = expression.Expression.Accept(this);
                if (CurrentFunction.ReturnType == eCobType.None)
                    CurrentFunction.ReturnType = rhs.Type;
                else if (CurrentFunction.ReturnType != rhs.Type)
                    messages.Add(Message.ReturnTypeMismatch, expression);
                
                CurrentFunction.Body.EmitO(Opcode.Return, rhs.Operand);
                rhs.Free();
            }

            return null;
        }

        public Storage? Visit(AheadOfTimeExpression expression)
        {
            var function = new Function(
                "$aot_eval$",
                CurrentModule,
                CallingConvention.CCall,
                Array.Empty<Function.Parameter>(),
                CobType.None
            );

            functionStack.Push(function);
            var evalStorage = expression.Expression.Accept(this);
            if (evalStorage == null || evalStorage.Type == eCobType.None)
                messages.Add(Message.AotCannotUseVoid, expression);

            function.ReturnType = evalStorage.Type;
            CurrentFunction.Body.EmitO(Opcode.Return, evalStorage.Operand);
            evalStorage.Free();

            function.ReturnLabel.Mark();
            
            functionStack.Pop();

            using var vm = new VirtualMachine(this);
            var result = vm.ExecuteFunction(function);

            var reg = CurrentFunction.AllocateStorage(function.ReturnType);
            // TODO EmitOI
            CurrentFunction.Body.EmitOO(
                Opcode.Move,
                reg.Operand,
                new Operand { Type = OperandType.ImmediateUnsigned, Size = -1, Value = result } // TODO Not right
            );
            
            return evalStorage;
        }

        public Storage? Visit(FatArrowExpression expression)
        {
            var value = expression.Expression.Accept(this);
            if (CurrentModule == rootModule && value != null && value.Type == eCobType.Function)
            {
                if (EntryFunction != null)
                    messages.Add(Message.CannotRedeclareEntryPoint, expression);
                else
                    EntryFunction = value.Type.Function;
            }

            return value;
        }

        public Storage? Visit(FunctionExpression expression)
        {
            if (expression.Body == null)
            {
                messages.Add(Message.MissingFunctionBody, expression);
                return null;
            }

            var function = new Function(
                expression.Name,
                CurrentModule,
                expression.CallingConvention,
                expression.Parameters,
                expression.ReturnType
            );

            var type = new CobType(eCobType.Function, -1, function: function);
            if (!expression.IsAnonymous)
                AllocateGlobal(new CobVariable(expression.Name, type, false));
            
            CurrentModule.Functions.Add(function);

            functionStack.Push(function);
            
            expression.Body.Accept(this);
            function.Body.Emit(Opcode.Return); // TODO Error if not all paths return

            function.ReturnLabel.Mark();
            
            functionStack.Pop();

            function.Body.HACK_Optmize();

            return new Storage(
                CurrentFunction,
                null, // TODO ???
                type
            );
        }

        public Storage? Visit(BinaryOperatorExpression expression)
        {
            if (expression.Operator == TokenType.Dot)
            {
                var lhs = expression.Left.Accept(this);
                contextStack.Push(Modules[(int)lhs.Operand.Value]); // TODO Validate type, support  structs, etc.
                var rhs = expression.Right.Accept(this);
                contextStack.Pop();
                lhs.Free();
                return rhs;
            }

            var a = expression.Left.Accept(this);
            var b = expression.Right.Accept(this);
            var cType = a.Type; // TODO Verify types

            var c = CurrentFunction.AllocateStorage(cType);

            // Assignment
            var asnOpcode = expression.Operator switch
            {
                TokenType.AddAssign => Opcode.Add,
                TokenType.SubtractAssign => Opcode.Sub,
                TokenType.MultiplyAssign => Opcode.Mul,
                TokenType.DivideAssign => Opcode.Div,
                TokenType.ModuloAssign => Opcode.Mod,
                TokenType.BitLeftShiftAssign => Opcode.BitShl,
                TokenType.BitRightShiftAssign => Opcode.BitShr,
                TokenType.BitAndAssign => Opcode.BitAnd,
                TokenType.BitOrAssign => Opcode.BitOr,
                TokenType.BitXorAssign => Opcode.BitXor,
                TokenType.Assign => Opcode.Move,
                _ => Opcode.None
            };

            // Equality
            var cmpOpcode = expression.Operator switch
            {
                TokenType.Equals => Opcode.JumpIfFalse,
                TokenType.NotEquals => Opcode.JumpIfTrue,
                TokenType.MoreThan => Opcode.JumpIfLessThanOrEqual,
                TokenType.MoreThanOrEquals => Opcode.JumpIfLessThan,
                TokenType.LessThan => Opcode.JumpIfMoreThanOrEqual,
                TokenType.LessThanOrEquals => Opcode.JumpIfMoreThan,
                _ => Opcode.None
            };

            // Arithmetic
            var artOpcode = expression.Operator switch
            {
                TokenType.Add => Opcode.Add,
                TokenType.Subtract => Opcode.Sub,
                TokenType.Multiply => Opcode.Mul,
                TokenType.Divide => Opcode.Div,
                TokenType.Modulo => Opcode.Mod,
                TokenType.BitLeftShift => Opcode.BitShl,
                TokenType.BitRightShift => Opcode.BitShr,
                TokenType.BitAnd => Opcode.BitAnd,
                TokenType.BitOr => Opcode.BitOr,
                TokenType.BitXor => Opcode.BitXor,
                _ => Opcode.None //throw new ArgumentOutOfRangeException(nameof(expression))
            };

            if (cmpOpcode != Opcode.None)
            {
                // TODO There needs to be a more concise opcode here
                var labelElse = CurrentFunction.Body.AllocateLabel();
                var labelEnd = CurrentFunction.Body.AllocateLabel();

                CurrentFunction.Body.EmitOO(Opcode.Move, c.Operand, a.Operand);
                CurrentFunction.Body.EmitOO(Opcode.Compare, c.Operand, b.Operand);
                CurrentFunction.Body.EmitL(cmpOpcode, labelElse);

                var const0 = CurrentFunction.AllocateStorage(CobType.Int, 0);
                CurrentFunction.Body.EmitOO(Opcode.Move, c.Operand, const0.Operand);
                CurrentFunction.Body.EmitL(Opcode.Jump, labelEnd);

                labelElse.Mark();

                var const1 = CurrentFunction.AllocateStorage(CobType.Int, 1);
                CurrentFunction.Body.EmitOO(Opcode.Move, c.Operand, const1.Operand);

                labelEnd.Mark();
            }
            else if (artOpcode != Opcode.None)
            {
                // HACK The mov instructions are because the x86 CG does not support certain mem, mem operands
                CurrentFunction.Body.EmitOO(Opcode.Move, c.Operand, a.Operand);
                CurrentFunction.Body.EmitOO(artOpcode, c.Operand, b.Operand);
            }
            else if (asnOpcode != Opcode.None)
            {
                var cobVariable = ResolveVariableFromOperand(a.Operand);
                if (cobVariable == null)
                {
                    messages.Add(Message.IllegalAssignment, expression);
                    return null;
                }
                else if (!cobVariable.Mutable)
                {
                    messages.Add(Message.IllegalAssignmentImmutable, expression);
                    return null;
                }

                // HACK The mov instructions are because the x86 CG does not support certain mem, mem operands
                CurrentFunction.Body.EmitOO(Opcode.Move, c.Operand, a.Operand);
                CurrentFunction.Body.EmitOO(asnOpcode, c.Operand, b.Operand);
                CurrentFunction.Body.EmitOO(Opcode.Move, a.Operand, c.Operand);

                //CurrentFunction.Body.EmitOO(asnOpcode, a.Operand, b.Operand);
                c.Free();
                c = a;
            }
            else
                throw new ArgumentOutOfRangeException(nameof(expression));

            a.Free();
            b.Free();

            return c;
        }

        public Storage? Visit(BlockExpression expression)
        {
            for (var i = 0; i < expression.Expressions.Count; i++)
            {
                var expr = expression.Expressions[i];
                var retStorage = expr.Accept(this);
                retStorage?.Free();
            }

            return null;
        }

        public Storage? Visit(CallExpression expression)
        {
            // CAST OPERATOR
            var castType = VisitCast(expression);
            if (castType != null)
                return castType;

            // FUNCTION IDENTIFIER
            var functionStorage = expression.FunctionExpression.Accept(this);
            var function = functionStorage?.Type.Function;
            if (function == null)
            {
                messages.Add(Message.CannotCallType, expression, functionStorage?.Type.ToString() ?? "(null)");
                return null;
            }
            
            // ARGUMENTS
            var parameters = function.Parameters;
            var arguments = expression.Arguments;
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
                    var argStorage = arguments[i].Accept(this);

                    if (paramType != null && paramType.IsSpread) paramType = null;
                    if (argStorage == null
                    ||  (paramType != null && !CobType.IsCastable(argStorage.Type, paramType.Type)))
                    {
                        aOperandArguments[i] = Operand.None;
                        messages.Add(Message.ParameterTypeMismatch, arguments[i], paramType?.Type, argStorage);
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
            CurrentFunction.Body.EmitOA(Opcode.Call, functionStorage.Operand, operandArguments);

            // CLEANUP
            functionStorage.Free(); // function reg
            if (operandArguments != null)
            {
                for (int i = 0; i < operandArguments.Count; ++i) // argument regs
                    CurrentFunction.FreeStorage(operandArguments[i]);
            }

            // RETURN VALUE
            Storage? retStorage = null;
            if (function.ReturnType != eCobType.None)
            {
                // TODO Can't just clobber reg 0 like this
                retStorage = CurrentFunction.AllocateStorage(function.ReturnType);
                CurrentFunction.Body.EmitOO(
                    Opcode.Move,
                    retStorage.Operand,
                    new Operand { Type = OperandType.Register, Value = 0, Size = function.ReturnType.Size }
                );
            }

            return retStorage;
        }

        private Storage? VisitCast(CallExpression expression)
        {
            if (expression.FunctionExpression is not IdentifierExpression ie)
                return null;
            
            if (!CobType.TryParse(ie.Value, out var castType))
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
                return source with { Type = dstType };
            }
            else
                throw new NotImplementedException();
        }

        public Storage? Visit(IdentifierExpression expression)
        {
            int idx;

            // TODO AllocateStorage(Type, Value, Origin)..?

            if (CurrentFunction != null)
            {
                // ARGUMENTS
                if ((idx = CurrentFunction.FindParameter(expression.Value)) != -1)
                {
                    var type = CurrentFunction.Parameters[idx];
                    return new Storage(
                        CurrentFunction,
                        new Operand
                        {
                            Type = OperandType.Argument,
                            Value = idx,
                            Size = type.Type.Size
                        },
                        type.Type
                    );
                }

                // LOCALS
                if ((idx = CurrentFunction.FindLocal(expression.Value)) != -1)
                {
                    var type = CurrentFunction.Locals[idx];
                    return new Storage(
                        CurrentFunction,
                        new Operand
                        {
                            Type = OperandType.Local,
                            Value = idx,
                            Size = type.Type.Size
                        },
                        type.Type
                    );
                }
            }

            // GLOBALS
            if (CurrentContext.Variables.TryGetValue(expression.Value, out var global)
            &&  (idx = FindGlobal(global)) != -1)
            {
                if (!IsSymbolVisible(global))
                {
                    messages.Add(Message.CannotAccessPrivateSymbol, expression, expression.Value, CurrentModule.Name ?? "(root)");
                    return null;
                }

                var type = Globals[idx];
                return new Storage(
                    CurrentFunction,
                    new Operand
                    {
                        Type = OperandType.Global,
                        Value = idx,
                        Size = type.Type.Size
                    },
                    type.Type
                );
            }

            // MODULES
            if ((idx = Modules.FindIndex(x => x.Name == expression.Value)) != -1)
            {
                return new Storage(
                    CurrentFunction,
                    new Operand
                    {
                        Type = OperandType.None, // TODO Module Type?
                        Value = idx,
                        Size = 0
                    },
                    CobType.None
                );
            }

            // FUCNTIONS
            //if ((idx = Functions.FindIndex(x => x.Name == expression.Value)) != -1)
            //{
            //    CurrentFunction.Body.EmitRF(Opcode.Move, reg, idx);
            //    return new CobType(eCobType.Function, 0, function: Functions[idx]);
            //}

            messages.Add(Message.UndeclaredIdentifier, expression, expression.Value);

            return null;
        }

        public Storage? Visit(NumberExpression expression)
        {
            return CurrentFunction.AllocateStorage(
                new CobType(expression.Type, expression.BitSize),
                expression.LongValue
            );
        }

        public Storage? Visit(StringExpression expression)
        {
            var byteCount = Encoding.UTF8.GetByteCount(expression.Value);
            var data = new byte[byteCount + 1];
            Encoding.UTF8.GetBytes(expression.Value, 0, expression.Value.Length, data, 0);
            
            var global = AllocateGlobal(new CobVariable(
                $"string{Globals.Count}",
                CobType.String,
                false
            ) { Data = data });
            
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

        [Obsolete]
        public int FindGlobal(string name)
        {
            return Globals.FindIndex(x => x.Name == name);
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

            CurrentModule.Variables[variable.Name] = variable;

            return idx;
        }

        private CobVariable? ResolveVariableFromOperand(Operand operand)
        {
            return operand.Type switch
            {
                OperandType.Local => CurrentFunction?.Locals.ElementAtOrDefault((int)operand.Value),
                OperandType.Global => Globals.ElementAtOrDefault((int)operand.Value),
                OperandType.Argument => null, // TODO This should be possible
                _ => null
            };
        }

        private bool IsSymbolVisible(CobVariable variable)
        {
            if (CurrentFunction != null && CurrentFunction.Module == CurrentContext)
                return true;

            return variable.Name.Length > 0 && char.IsUpper(variable.Name[0]);
        }
    }

    internal sealed record Import
    {
        public string Library { get; init; }

        public string? SymbolName { get; init; }

        public Function? Function { get; init; }
    }

    internal sealed record Module
    {
        public string? Name { get; init; }

        public List<Function> Functions { get; } = new ();

        public Dictionary<string, CobVariable> Variables { get; } = new ();
    }
}
