using System.Text;
using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Ast.Visitors;
using Compiler.Interpreter;
using Compiler.Lexer;

namespace Compiler.CodeGeneration
{
    internal sealed class Compiler : IExpressionVisitor<CobType?>
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
        
        public Compiler()
        {
            Artifacts = new List<ArtifactExpression>();
            Imports = new List<Import>();
            Exports = new Dictionary<string, string>();
            Functions = new List<Function>();
            Globals = new List<CobVariable>();
            functionStack = new Stack<Function>();
        }

        public CobType? Visit(ScriptExpression expression)
        {
            var expressions = expression.Expressions;
            for (int i = 0; i < expressions.Count; ++i)
                expressions[i].Accept(this);

            return null;
        }

        public CobType? Visit(VarExpression expression)
        {
            for (var i = 0; i < expression.Declarations.Count; ++i)
            {
                var decl = expression.Declarations[i];
                var rhsType = decl.Initializer?.Accept(this);

                if (CurrentFunction != null && rhsType != null) // Local Decl
                {
                    var local = CurrentFunction.AllocateLocal(new CobVariable(decl.Name, rhsType));
                    var reg = CurrentFunction.FreeRegister();
                    CurrentFunction.Body.EmitLR(Opcode.Move, local, reg);
                }
                else if (CurrentFunction == null && rhsType != null) // Global Decl
                {
                    //rhsType.Function.Name = decl.Name;
                    AllocateGlobal(new CobVariable(decl.Name, rhsType));

                    // TODO Throw exception if export declared outside root level
                    // TODO Throw exception if export declared on non-function?
                    if (expression.Type == TokenType.Export)
                        Exports.Add(decl.Name, rhsType.Function.Name);
                }
            }

            return null;
        }

        public CobType? Visit(ImportExpression expression)
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

        public CobType? Visit(ArtifactExpression expression)
        {
            Artifacts.Add(expression);
            return null;
        }

        public CobType? Visit(ReturnStatement expression)
        {
            if (expression.Expression == null)
                CurrentFunction.Body.Emit(Opcode.Return);
            else
            {
                var rhsType = expression.Expression.Accept(this);
                if (CurrentFunction.ReturnType == eCobType.None)
                    CurrentFunction.ReturnType = rhsType;
                else if (CurrentFunction.ReturnType != rhsType)
                    throw new Exception("Return value does not match function return type"); // TODO
                
                var reg = CurrentFunction.FreeRegister();
                CurrentFunction.Body.EmitR(Opcode.Return, reg);
            }

            return null;
        }

        public CobType? Visit(AheadOfTimeExpression expression)
        {
            var function = new Function(
                "$aot_eval$",
                CallingConvention.CCall,
                Array.Empty<Function.Parameter>(),
                CobType.None
            );

            functionStack.Push(function);
            var evalType = expression.Expression.Accept(this);
            if (evalType == null || evalType == eCobType.None)
                throw new Exception("Cannot evaluate ahead-of-time expression on void");
            function.ReturnType = evalType;
            var retReg = CurrentFunction.FreeRegister();
            CurrentFunction.Body.EmitR(Opcode.Return, retReg);

            function.ReturnLabel.Mark();
            function.Body.FixLabels();
            
            functionStack.Pop();

            using var vm = new VirtualMachine(this);
            var result = vm.ExecuteFunction(function);

            var reg = CurrentFunction.AllocateRegister();
            CurrentFunction.Body.EmitRI(Opcode.Move, reg, result);
            
            return evalType;
        }

        public CobType? Visit(FunctionExpression expression)
        {
            var function = new Function(
                expression.Name,
                expression.CallingConvention,
                expression.Parameters,
                CobType.FromString(expression.ReturnType)
            );

            functionStack.Push(function);
            
            expression.Body?.Accept(this);
            function.Body.Emit(Opcode.Return);

            function.ReturnLabel.Mark();
            function.Body.FixLabels();
            
            functionStack.Pop();
            Functions.Add(function);
            
            return new CobType(eCobType.Function, 0, function: function);
        }

        public CobType? Visit(BinaryOperatorExpression expression)
        {
            expression.Left.Accept(this);
            expression.Right.Accept(this);

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

            var b = CurrentFunction.FreeRegister();
            var a = CurrentFunction.FreeRegister();
            CurrentFunction.Body.EmitRR(opcode, a, b);

            var c = CurrentFunction.AllocateRegister();
            CurrentFunction.Body.EmitRR(Opcode.Move, c, a);

            return new CobType(eCobType.Signed, 32);
        }

        public CobType? Visit(BlockExpression expression)
        {
            for (var i = 0; i < expression.Expressions.Count; i++)
            {
                var expr = expression.Expressions[i];
                expr.Accept(this);
            }

            return null;
        }

        public CobType? Visit(CallExpression expression)
        {
            var functionType = expression.FunctionExpression.Accept(this);
            var function = functionType?.Function;
            if (function == null)
                throw new Exception($"Cannot call type '{functionType}'");

            var parameters = function.Parameters;
            var arguments = expression.Arguments;
            if (arguments.Count != parameters.Count
            && (parameters.Count == 0 || (parameters.Count > 0 && !parameters[^1].IsSpread)))
                throw new Exception($"Expected {parameters.Count} parameters, but received {arguments.Count} instead");

            if (function.CallingConvention != CallingConvention.CCall
            && (parameters.Count > 0 && !parameters[^1].IsSpread))
                throw new Exception("Cannot use spread parameters without ccall");

            // TODO Calling convention?

            var stackSpace = 0;
            for (int i = arguments.Count - 1; i >= 0; --i)
            {
                var argType = arguments[i].Accept(this);
                // TODO
                //if (argType != CobType.FromString(parameters[i].Type))
                //    throw new Exception($"Expected '{parameters[i].Type}' but received '{argType}' instead");

                var reg = CurrentFunction.FreeRegister();
                CurrentFunction.Body.EmitR(Opcode.Push, reg);

                stackSpace += (argType.Size + 7) / 8;
            }
            
            var freg = CurrentFunction.FreeRegister();
            
            CurrentFunction.Body.EmitR(Opcode.Call, freg);
            
            if (function.ReturnType != eCobType.None)
            {
                var reg = CurrentFunction.AllocateRegister();
                CurrentFunction.Body.EmitRR(Opcode.Move, reg, 0);
                
                if (function.CallingConvention == CallingConvention.CCall)
                    CurrentFunction.Body.EmitI(Opcode.RestoreStack, stackSpace);
            }
            
            return function.ReturnType;
        }

        public CobType? Visit(IdentifierExpression expression)
        {
            int idx;

            var reg = CurrentFunction.AllocateRegister();

            // PARAMETERS
            if ((idx = CurrentFunction.FindParameter(expression.Value)) != -1)
            {
                CurrentFunction.Body.EmitRP(Opcode.Move, reg, idx);
                return CurrentFunction.Parameters[idx].Type;
            }

            // LOCALS
            if ((idx = CurrentFunction.FindLocal(expression.Value)) != -1)
            {
                CurrentFunction.Body.EmitRL(Opcode.Move, reg, idx);
                return CurrentFunction.Locals[idx].Type;
            }

            // GLOBALS
            if ((idx = FindGlobal(expression.Value)) != -1)
            {
                CurrentFunction.Body.EmitRG(Opcode.Move, reg, idx);
                return Globals[idx].Type;
            }

            // FUCNTIONS
            if ((idx = Functions.FindIndex(x => x.Name == expression.Value)) != -1)
            {
                CurrentFunction.Body.EmitRF(Opcode.Move, reg, idx);
                return new CobType(eCobType.Function, 0, function: Functions[idx]);
            }

            throw new Exception($"Undeclared identifier {expression.Value}");

            return null;
        }

        public CobType? Visit(NumberExpression expression)
        {
            var type = new CobType(expression.Type, expression.BitSize);
            var reg = CurrentFunction.AllocateRegister();

            if (type == eCobType.Float)
            {
                var globalIdx = AllocateGlobal(new CobVariable($"float{Globals.Count}", type)
                {
                    Value = expression.LongValue
                });

                CurrentFunction.Body.EmitRG(Opcode.Move, reg, globalIdx);
            }
            else
                CurrentFunction.Body.EmitRI(Opcode.Move, reg, expression.LongValue);
            
            return type;
        }

        public CobType? Visit(StringExpression expression)
        {
            var byteCount = Encoding.UTF8.GetByteCount(expression.Value);
            var data = new byte[byteCount + 1];
            Encoding.UTF8.GetBytes(expression.Value, 0, expression.Value.Length, data, 0);

            var type = new CobType(
                eCobType.Array,
                expression.Value.Length,
                new CobType(eCobType.Unsigned, 8)
            );

            var global = AllocateGlobal(new CobVariable(
                $"string{Globals.Count}",
                type
            ) { Data = data });

            var reg = CurrentFunction.AllocateRegister();
            CurrentFunction.Body.EmitRG(Opcode.Move, reg, global);

            return type;
        }

        public CobType? Visit(EmptyExpression expression)
        {
            return null;
        }

        public int FindGlobal(string name)
        {
            return Globals.FindIndex(x => x.Name == name);
        }
        
        private int AllocateGlobal(CobVariable variable)
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
