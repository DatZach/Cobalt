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

        public Dictionary<string, string> Exports { get; } // (ExternalName, InternalName)

        public List<Function> Functions { get; }

        // TODO Optimization
        //public Dictionary<string, CobVariable> Globals { get; }
        public List<CobVariable> Globals { get; }

        private Function? CurrentFunction => functionStack.Count == 0 ? null : functionStack.Peek();

        private readonly Stack<Function> functionStack;
        private readonly MessageCollection messages;
        
        public Compiler(MessageCollection messages)
        {
            Artifacts = new List<ArtifactExpression>();
            Imports = new List<Import>();
            Exports = new Dictionary<string, string>();
            Functions = new List<Function>();
            Globals = new List<CobVariable>();
            functionStack = new Stack<Function>();

            this.messages = messages ?? throw new ArgumentNullException(nameof(messages));
        }

        public Storage? Visit(ScriptExpression expression)
        {
            var expressions = expression.Expressions;
            for (int i = 0; i < expressions.Count; ++i)
                expressions[i].Accept(this);

            return null;
        }

        public Storage? Visit(VarExpression expression)
        {
            for (var i = 0; i < expression.Declarations.Count; ++i)
            {
                var decl = expression.Declarations[i];
                var rhs = decl.Initializer?.Accept(this);

                if (CurrentFunction != null && rhs != null) // Local Decl
                {
                    var local = CurrentFunction.AllocateLocal(new CobVariable(decl.Name, rhs.Type));
                    CurrentFunction.Body.EmitOO(
                        Opcode.Move,
                        new Operand { Type = OperandType.Local, Value = local, Size = (byte)rhs.Type.Size },
                        rhs.Operand
                    ); // TODO EmitLO
                }
                else if (CurrentFunction == null && rhs != null) // Global Decl
                {
                    //rhsType.Function.Name = decl.Name;
                    AllocateGlobal(new CobVariable(decl.Name, rhs.Type));

                    // TODO Throw exception if export declared outside root level
                    // TODO Throw exception if export declared on non-function?
                    if (expression.Type == TokenType.Export)
                        Exports.Add(decl.Name, rhs.Type.Function.Name);
                }

                rhs?.Free();
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
                        new CobType(eCobType.Function, 0, function: function)
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
            function.Body.FixLabels();
            
            functionStack.Pop();

            using var vm = new VirtualMachine(this);
            var result = vm.ExecuteFunction(function);

            var reg = CurrentFunction.AllocateStorage(function.ReturnType);
            // TODO EmitOI
            CurrentFunction.Body.EmitOO(
                Opcode.Move,
                reg.Operand,
                new Operand { Type = OperandType.ImmediateUnsigned, Size = (byte)64, Value = result } // TODO Not right
            );
            
            return evalStorage;
        }

        public Storage? Visit(FunctionExpression expression)
        {
            var function = new Function(
                expression.Name,
                expression.CallingConvention,
                expression.Parameters,
                expression.ReturnType
            );

            functionStack.Push(function);
            
            expression.Body?.Accept(this);
            function.Body.Emit(Opcode.Return); // TODO Error if not all paths return

            function.ReturnLabel.Mark();
            function.Body.FixLabels();
            
            functionStack.Pop();
            Functions.Add(function);

            function.Body.HACK_Optmize();

            return new Storage(
                CurrentFunction,
                null, // TODO ???
                new CobType(eCobType.Function, 0, function: function)
            );
        }

        public Storage? Visit(BinaryOperatorExpression expression)
        {
            var a = expression.Left.Accept(this);
            var b = expression.Right.Accept(this);
            var cType = a.Type; // TODO Verify types

            var opcode = expression.Operator switch
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
                _ => throw new ArgumentOutOfRangeException(nameof(expression))
            };
            
            var c = CurrentFunction.AllocateStorage(cType);
            CurrentFunction.Body.EmitOO(Opcode.Move, c.Operand, a.Operand);
            CurrentFunction.Body.EmitOO(opcode, c.Operand, b.Operand);
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
                messages.Add(Message.CannotCallType, expression, functionStorage?.Type);
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
                    if (paramType != null && !CobType.IsCastable(argStorage.Type, paramType.Type))
                        messages.Add(Message.ParameterTypeMismatch, arguments[i], paramType.Type, argStorage);
                    
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
                    new Operand { Type = OperandType.Register, Value = 0, Size = (byte)function.ReturnType.Size }
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
            if (srcType == eCobType.Unsigned
            ||  srcType == eCobType.Signed
            ||  srcType == eCobType.Float)
            {
                var target = CurrentFunction.AllocateStorage(dstType);
                CurrentFunction.Body.EmitOO(Opcode.Move, target.Operand, source.Operand);
                source.Free();
                return target;
            }
            else
                throw new NotImplementedException();
        }

        public Storage? Visit(IdentifierExpression expression)
        {
            int idx;

            // TODO AllocateStorage(Type, Value, Origin)..?
            
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
                        Size = (byte)type.Type.Size
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
                        Size = (byte)type.Type.Size
                    },
                    type.Type
                );
            }

            // GLOBALS
            if ((idx = FindGlobal(expression.Value)) != -1)
            {
                var type = Globals[idx];
                return new Storage(
                    CurrentFunction,
                    new Operand
                    {
                        Type = OperandType.Global,
                        Value = idx,
                        Size = (byte)type.Type.Size
                    },
                    type.Type
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
                CobType.String
            ) { Data = data });
            
            // TODO AllocateStorage on Global "function"??
            return new Storage(
                CurrentFunction,
                new Operand
                {
                    Type = OperandType.Global,
                    Value = global
                },
                CobType.String
            );
        }

        public Storage? Visit(EmptyExpression expression)
        {
            return null;
        }

        public int FindGlobal(string name)
        {
            return Globals.FindIndex(x => x.Name == name);
        }
        
        public int AllocateGlobal(CobVariable variable)
        {
            if (variable == null) throw new ArgumentNullException(nameof(variable));

            if (FindGlobal(variable.Name) != -1)
                return -1;

            var idx = Globals.Count;
            Globals.Add(variable);

            return idx;
        }
    }

    internal sealed record Import
    {
        public string Library { get; init; }

        public string? SymbolName { get; init; }

        public Function? Function { get; init; }
    }
}
