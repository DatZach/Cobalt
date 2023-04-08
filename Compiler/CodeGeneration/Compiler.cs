using System.Text;
using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Ast.Visitors;
using Compiler.Lexer;

namespace Compiler.CodeGeneration
{
    internal sealed class Compiler : IExpressionVisitor<int>
    {
        public Artifact? Artifact { get; private set; }

        public List<Import> Imports { get; }

        public List<string> Exports { get; }

        public List<Function> Functions { get; }

        // TODO Optimization
        //public Dictionary<string, CobVariable> Globals { get; }
        public List<CobVariable> Globals { get; }

        private Function? CurrentFunction => functionStack.Count == 0 ? null : functionStack.Peek();

        private readonly Stack<Function> functionStack;

        private CobVariable? tmpValue; // HACK TODO Find a better way to pass back variables from RHS

        public Compiler()
        {
            Functions = new List<Function>();
            Imports = new List<Import>();
            Exports = new List<string>();
            //Globals = new Dictionary<string, CobVariable>();
            Globals = new List<CobVariable>();
            functionStack = new Stack<Function>();
        }

        public int Visit(ScriptExpression expression)
        {
            var expressions = expression.Expressions;
            for (int i = 0; i < expressions.Count; ++i)
                expressions[i].Accept(this);

            return 0;
        }

        public int Visit(VarExpression expression)
        {
            foreach (var decl in expression.Declarations)
            {
                tmpValue = null;
                decl.Initializer?.Accept(this);

                if (CurrentFunction != null && tmpValue != null)
                {
                    tmpValue.Name = decl.Name;
                    var local = CurrentFunction.AllocateLocal(tmpValue);
                    var reg = CurrentFunction.FreeRegister();
                    CurrentFunction.Body.EmitLR(Opcode.Move, local, reg);
                }
                else if (CurrentFunction == null && tmpValue != null)
                {
                    tmpValue.Name = decl.Name;
                    tmpValue.Type.Function.Name = decl.Name;
                    AllocateGlobal(tmpValue);
                }
                
                if (expression.Type == TokenType.Export)
                    Exports.Add(decl.Name);
            }

            return 0;
        }

        public int Visit(ImportExpression expression)
        {
            Imports.Add(new Import
            {
                Library = expression.Library,
                SymbolName = expression.SymbolName,
                //FunctionSignature = expression.FunctionSignture
            });

            if (expression.SymbolName != null)
            {
                AllocateGlobal(new CobVariable(expression.SymbolName, new CobType(CobPrimitive.Function, 0)));

                if (expression.FunctionSignture != null)
                {
                    Functions.Add(new Function(
                        expression.SymbolName,
                        expression.FunctionSignture.CallingConvention,
                        expression.FunctionSignture.Parameters,
                        new CobType(CobPrimitive.None, 0)
                    ));
                }
            }
            
            return 0;
        }

        public int Visit(ArtifactExpression expression)
        {
            Artifact = new Artifact
            {
                TargetPlatform = expression.TargetPlaform,
                Filename = expression.Filename
            };

            return 0;
        }

        public int Visit(ReturnStatement expression)
        {
            if (expression.Expression == null)
                CurrentFunction.Body.Emit(Opcode.Return);
            else
            {
                expression.Expression.Accept(this);
                if (CurrentFunction.ReturnType.Type == CobPrimitive.None)
                    CurrentFunction.ReturnType = tmpValue.Type;
                else if (CurrentFunction.ReturnType != tmpValue.Type)
                    throw new Exception("Return value does not match function return type"); // TODO
                
                var reg = CurrentFunction.FreeRegister();
                CurrentFunction.Body.EmitR(Opcode.Return, reg);
            }

            return 0;
        }

        public int Visit(AheadOfTimeExpression expression)
        {
            throw new NotImplementedException();
        }

        public int Visit(FunctionExpression expression)
        {
            var function = new Function(
                expression.Name,
                expression.CallingConvention,
                expression.Parameters,
                new CobType(CobPrimitive.None, 0) // TODO expression.ReturnType
            );

            functionStack.Push(function);
            
            expression.Body?.Accept(this);
            function.ReturnLabel.Mark();
            function.Body.FixLabels();
            
            functionStack.Pop();
            Functions.Add(function);

            tmpValue = new CobVariable(
                function.Name,
                new CobType(CobPrimitive.Function, 0, function: function)
            );

            return 0;
        }

        public int Visit(BinaryOperatorExpression expression)
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

            return 0;
        }

        public int Visit(BlockExpression expression)
        {
            for (var i = 0; i < expression.Expressions.Count; i++)
            {
                var expr = expression.Expressions[i];
                expr.Accept(this);
            }

            return 0;
        }

        public int Visit(CallExpression expression)
        {
            // TODO Parameters
            // TODO Calling convention?
            var args = expression.Arguments;
            for (int i = args.Count - 1; i >= 0; --i)
            {
                args[i].Accept(this);

                var reg = CurrentFunction.FreeRegister();
                CurrentFunction.Body.EmitR(Opcode.Push, reg);
            }

            // TODO Dive LHS expession
            var functionName = expression.FunctionExpression.Token.Value;
            var globalIdx = Functions.FindIndex(x => x.Name == functionName);
            if (globalIdx == -1) throw new Exception($"Undeclared identifier {functionName}");
            CurrentFunction.Body.EmitF(Opcode.Call, globalIdx);

            var callee = Functions[globalIdx];
            if (callee.ReturnType.Type != CobPrimitive.None)
            {
                tmpValue = new CobVariable("$return_value$", callee.ReturnType);
                var reg = CurrentFunction.AllocateRegister();
                CurrentFunction.Body.EmitRR(Opcode.Move, reg, 0);
            }
            
            return 0;
        }

        public int Visit(IdentifierExpression expression)
        {
            int idx = -1;

            var reg = CurrentFunction.AllocateRegister();

            // LOCALS
            if (CurrentFunction != null)
            {
                idx = CurrentFunction.FindLocal(expression.Value);
                CurrentFunction.Body.EmitRL(Opcode.Move, reg, idx);
                return 0;
            }

            // GLOBALS
            if (idx == -1)
            {
                idx = FindGlobal(expression.Value);
                CurrentFunction.Body.EmitRG(Opcode.Move, reg, idx);
                return 0;
            }

            throw new Exception($"Undeclared identifier {expression.Value}");
            
            return 0;
        }

        public int Visit(NumberExpression expression)
        {
            var reg = CurrentFunction.AllocateRegister();
            CurrentFunction.Body.EmitRI(Opcode.Move, reg, expression.LongValue);

            tmpValue = new CobVariable("literal", new CobType(CobPrimitive.Signed, 32));

            return 0;
        }

        public int Visit(StringExpression expression)
        {
            var byteCount = Encoding.UTF8.GetByteCount(expression.Value);
            var data = new byte[byteCount + 1];
            Encoding.UTF8.GetBytes(expression.Value, 0, expression.Value.Length, data, 0);

            tmpValue = new CobVariable(
                $"string{Globals.Count}",
                new CobType(
                    CobPrimitive.Array,
                    expression.Value.Length,
                    new CobType(CobPrimitive.Unsigned, 8)
                )
            ) { Data = data };

            var global = AllocateGlobal(tmpValue);

            var reg = CurrentFunction.AllocateRegister();
            CurrentFunction.Body.EmitRG(Opcode.Move, reg, global);

            return 0;
        }

        public int Visit(EmptyExpression expression)
        {
            return 0;
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

    public sealed record Artifact
    {
        public string TargetPlatform { get; init; }

        public string? Filename { get; init; }
    }

    internal enum Opcode
    {
        None,

        Call,
        Return,
        Jump,

        Move,
        Push,
        Pop,
        
        BitShr,
        BitShl,
        BitAnd,
        BitXor,
        BitOr,

        Add,
        Sub,
        Mul,
        Div,
        Mod,
    }

    public sealed record Operand
    {
        public OperandType Type { get; init; }

        public byte Size { get; init; }

        public long Value { get; set; } // TODO init only? need to mutate for Label

        public override string ToString()
        {
            return $"{Type.ToString()[..3]}{Value}.{Size}";
        }
    }

    public enum OperandType : byte
    {
        None,
        ImmediateSigned,
        ImmediateUnsigned,
        ImmediateFloat,
        Register,
        Pointer,
        Local,
        Global,
        Function
    }

    internal sealed record Instruction
    {
        public Opcode Opcode { get; init; }

        public Operand? A { get; init; }

        public Operand? B { get; init; }

        public override string ToString()
        {
            return $"{Opcode,-10}{A?.ToString() ?? ""} {B?.ToString() ?? ""}";
        }
    }

    internal sealed class InstructionBuffer
    {
        public byte PlatformWordWidth { get; }

        public List<Instruction> Instructions => instructions; // TODO Implement interface on base

        private readonly List<Label> labels;

        private readonly List<Instruction> instructions;

        public InstructionBuffer()
        {
            PlatformWordWidth = 32; // TODO Don't hardcode

            instructions = new List<Instruction>(4);
            labels = new List<Label>(4);
        }

        public void Emit(Opcode opcode)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode
            });
        }

        public void EmitR(Opcode opcode, int regA)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.Register, Size = PlatformWordWidth, Value = regA }
            });
        }

        public void EmitRR(Opcode opcode, int regA, int regB)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.Register, Size = PlatformWordWidth, Value = regA },
                B = new Operand { Type = OperandType.Register, Size = PlatformWordWidth, Value = regB }
            });
        }

        public void EmitRI(Opcode opcode, int regA, long immB)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.Register, Size = PlatformWordWidth, Value = regA },
                B = new Operand { Type = OperandType.ImmediateSigned, Size = PlatformWordWidth, Value = immB }
            });
        }

        public void EmitI(Opcode opcode, long immA)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.ImmediateSigned, Size = PlatformWordWidth, Value = immA }
            });
        }

        public void EmitI(Opcode opcode, ulong immA)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.ImmediateUnsigned, Size = PlatformWordWidth, Value = (long)immA }
            });
        }

        public void EmitI(Opcode opcode, double immA)
        {
            var value = BitConverter.DoubleToInt64Bits(immA);
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.ImmediateUnsigned, Size = PlatformWordWidth, Value = value }
            });
        }

        public void EmitLR(Opcode opcode, int locA, int regB)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.Local, Size = PlatformWordWidth, Value = locA },
                B = new Operand { Type = OperandType.Register, Size = PlatformWordWidth, Value = regB }
            });
        }

        public void EmitG(Opcode opcode, int globA)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.Global, Size = PlatformWordWidth, Value = globA }
            });
        }

        public void EmitRL(Opcode opcode, int regA, int locB)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.Register, Size = PlatformWordWidth, Value = regA },
                B = new Operand { Type = OperandType.Local, Size = PlatformWordWidth, Value = locB }
            });
        }

        public void EmitRG(Opcode opcode, int regA, int globB)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.Register, Size = PlatformWordWidth, Value = regA },
                B = new Operand { Type = OperandType.Global, Size = PlatformWordWidth, Value = globB }
            });
        }

        public void EmitF(Opcode opcode, int funA)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.Function, Size = PlatformWordWidth, Value = funA }
            });
        }

        public void FixLabels()
        {
            foreach (var label in labels)
                label.Fix();
        }
    }

    internal sealed class Label
    {
        private int labelOffset;

        private readonly InstructionBuffer buffer;
        private readonly List<int> patches;

        public Label(InstructionBuffer buffer)
        {
            this.buffer = buffer;
            patches = new List<int>();
            labelOffset = 0;
        }

        public void Mark()
        {
            labelOffset = buffer.Instructions.Count;
        }

        public void PatchHere()
        {
            //buffer.Instructions.Last().Operand.IntValue = 0;
            patches.Add(buffer.Instructions.Count - 1);
        }

        public void Fix()
        {
            for (var i = 0; i < patches.Count; i++)
            {
                var offset = patches[i];
                buffer.Instructions[offset].A!.Value = labelOffset;
            }
        }

        public void ClearPatches()
        {
            patches.Clear();
        }
    }

    internal sealed record Import
    {
        public string Library { get; init; }

        public string? SymbolName { get; init; }

        public Function? FunctionSignature { get; init; }
    }

    internal sealed class Function
    {
        public string Name { get; set; } // TODO HACK DO NOT ALLOW MUTATIONS

        public List<CobVariable> Locals { get; }

        public ulong ClobberedRegisters { get; private set; }

        public CallingConvention CallingConvention { get; }

        public IReadOnlyList<FunctionExpression.Parameter> Parameters { get; }

        public CobType ReturnType { get; set; }

        public InstructionBuffer Body { get; }

        public Label ReturnLabel { get; }

        private int registerStack;

        public Function(
            string name,
            CallingConvention callingConvention,
            IReadOnlyList<FunctionExpression.Parameter> parameters,
            CobType returnType
        ) {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Locals = new List<CobVariable>();
            ClobberedRegisters = 0;
            CallingConvention = callingConvention;
            Parameters = parameters;
            ReturnType = returnType;
            Body = new InstructionBuffer();
            ReturnLabel = new Label(Body);

            registerStack = 0;
        }

        public int AllocateRegister()
        {
            var register = registerStack++;
            ClobberedRegisters |= 1ul << register;

            return register;
        }

        public int FreeRegister()
        {
            return --registerStack;
        }

        public int PreserveRegister(int register)
        {
            if (register < registerStack)
            {
                Body.EmitR(Opcode.Push, register);
                return register;
            }

            return -1;
        }

        public void RestoreRegister(int register)
        {
            if (register == -1)
                return;

            Body.EmitR(Opcode.Pop, register);
        }

        public int AllocateLocal(CobVariable variable)
        {
            if (variable == null) throw new ArgumentNullException(nameof(variable));

            var idx = Locals.FindIndex(x => x.Name == variable.Name);
            if (idx == -1)
            {
                idx = Locals.Count;
                Locals.Add(variable);
            }

            return idx;
        }

        public int FindLocal(string name)
        {
            return Locals.FindIndex(x => x.Name == name);
        }
    }

    internal enum CallingConvention
    {
        None,
        CCall,
        Stdcall
    }

    internal sealed record CobVariable
    {
        public string Name { get; set; } // HACK set

        public CobType Type { get; }

        public byte[]? Data { get; set; } // TODO HACK AAAAAA???
        
        public long Value { get; set; }

        public CobVariable(string name, CobType type)
        {
            Name = name;
            Type = type;
        }

        public override string ToString()
        {
            return $"{Name,20}{Type} = {Data}";
        }
    }

    internal sealed record CobType
    {
        public CobPrimitive Type { get; }

        public int Size { get; }

        public CobType? ElementType { get; }

        public Function? Function { get; }

        public CobType(CobPrimitive type, int size, CobType? elementType = null, Function? function = null)
        {
            Type = type;
            Size = size;
            ElementType = elementType;
            Function = function;
        }

        public override string ToString()
        {
            if (ElementType != null)
                return $"{Type}.{Size}[{ElementType}]";
            return $"{Type}.{Size}";
        }
    }

    internal enum CobPrimitive
    {
        None,
        Signed,
        Unsigned,
        Float,
        Trait,
        Array,
        Function
    }
}
