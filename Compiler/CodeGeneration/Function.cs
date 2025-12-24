using Compiler.Ast.Expressions;
using Compiler.Interpreter;
using System.Diagnostics;

namespace Compiler.CodeGeneration
{
    [DebuggerDisplay("Function '{FullyQualifiedName}'")]
    internal sealed class Function : IContext, ISymbol
    {
        public string Name { get; }

        public IContext? Parent { get; }

        public List<CobVariable> Locals { get; }

        public uint ClobberedRegisters { get; private set; }

        public CallingConvention CallingConvention { get; }

        public Import? NativeImport { get; set; } // TODO init??

        public IReadOnlyList<Parameter> Parameters { get; }

        public CobType ReturnType { get; set; }

        public InstructionBuffer Body { get; }

        public Label ReturnLabel { get; }

        public string FullyQualifiedName => (Parent?.Name ?? "root") + '_' + Name;

        private readonly Compiler compiler;

        private int freeRegisterIndex;
        private int registers;

        public Function(
            string name,
            Compiler compiler,
            IContext parent,
            CallingConvention callingConvention,
            IReadOnlyList<Parameter> parameters,
            CobType returnType
        )
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Parent = parent ?? throw new ArgumentNullException(nameof(parent));
            Locals = new List<CobVariable>();
            ClobberedRegisters = 0;
            CallingConvention = callingConvention;
            Parameters = parameters;
            ReturnType = returnType;
            Body = new InstructionBuffer();
            ReturnLabel = Body.AllocateLabel();

            this.compiler = compiler;

            freeRegisterIndex = 0;
            registers = 0;
        }

        public Storage AllocateStorage(CobType type, long value)
        {
            // TODO Pool
            var operandType = type.Type switch // TODO ???
            {
                eCobType.Signed => OperandType.ImmediateSigned,
                eCobType.Unsigned => OperandType.ImmediateUnsigned,
                eCobType.Float => OperandType.ImmediateFloat,
                eCobType.Tuple => OperandType.Local, // ????
                eCobType.Struct => OperandType.Local, // ????
                _ => throw new NotSupportedException()
            };

            var operand = new Operand
            {
                Type = operandType,
                Value = value,
                Size = type.Size
            };

            return new Storage(this, operand, type);
        }

        public Storage AllocateStorage(CobType type)
        {
            // TODO Pool
            var register = type.Type switch // TODO ???
            {
                eCobType.Signed => AllocateRegister(),
                eCobType.Unsigned => AllocateRegister(),
                eCobType.Float => AllocateRegister(),
                eCobType.Array => AllocateRegister(), // ????????
                eCobType.Lens => AllocateRegister(), // ??
                eCobType.Tuple => AllocateRegister(),//AllocateLocal(new CobVariable("$tuple", type, true)),
                eCobType.Struct => AllocateRegister(),
                _ => throw new NotSupportedException()
            };

            var operand = new Operand
            {
                Type = OperandType.Register,
                Value = register,
                Size = type.Size
            };

            return new Storage(this, operand, type);
        }

        public void FreeStorage(Operand storage)
        {
            switch (storage.Type)
            {
                case OperandType.Register:
                    FreeRegister((int)storage.Value);
                    break;

                default:
                    // NOTE Nothing to do?
                    break;
            }
        }

        private int AllocateRegister()
        {
            var i = freeRegisterIndex;
            while ((registers & (1 << i)) != 0 && i < VirtualMachine.MaxRegisters)
                ++i;

            if (i >= VirtualMachine.MaxRegisters)
                throw new InvalidOperationException("Exhausted registers!");

            registers |= (1 << i);
            freeRegisterIndex = i + 1;
            ClobberedRegisters |= 1u << i;

            return i;
        }

        private void FreeRegister(int register)
        {
            registers &= ~(1 << register);
            freeRegisterIndex = register;
        }
        
        //public int PreserveRegister(int register)
        //{
        //    if (register < freeRegisterIndex)
        //    {
        //        Body.EmitR(Opcode.Stash, register);
        //        return register;
        //    }

        //    return -1;
        //}

        //public void RestoreRegister(int register)
        //{
        //    if (register == -1)
        //        return;

        //    Body.EmitR(Opcode.Unstash, register);
        //}

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

        public int FindParameter(string name)
        {
            for (int i = 0; i < Parameters.Count; ++i)
            {
                if (Parameters[i].Name == name)
                    return i;
            }

            return -1;
        }

        public ISymbol? FindIdentifier(string name)
        {
            Parameter? parameter;
            if ((parameter = Parameters.FirstOrDefault(x => x.Name == name)) != null)
                return parameter;

            CobVariable? local;
            if ((local = Locals.FirstOrDefault(x => x.Name == name)) != null)
                return local;

            return null;
        }

        public Storage? EmitGetIdentifier(ISymbol identifier)
        {
            if (identifier is Parameter parameter)
            {
                var idx = FindParameter(parameter.Name); // TODO Bit odd to search on name again like this
                return new Storage(
                    this,
                    new Operand
                    {
                        Type = OperandType.Argument,
                        Value = idx,
                        Size = parameter.Type.Size
                    },
                    parameter.Type
                );
            }

            if (identifier is CobVariable variable)
            {
                var idx = Locals.IndexOf(variable);
                return new Storage(
                    this,
                    new Operand
                    {
                        Type = OperandType.Local,
                        Value = idx,
                        Size = variable.Type.Size
                    },
                    variable.Type
                );
            }

            return null;
        }

        public bool EmitSetIdentifier(ISymbol identifier)
        {
            // ARGUMENTS
            if (identifier is Parameter parameter)
            {
                var idx = FindParameter(parameter.Name);

                compiler.CurrentFunction.Body.Emit(
                    Opcode.Move,
                    new Operand
                    {
                        Type = OperandType.Argument,
                        Value = idx,
                        Size = parameter.Type.Size
                    },
                    compiler.AssignmentRHS.Operand
                );
                return true;
            }

            // LOCALS
            if (identifier is CobVariable local)
            {
                var idx = Locals.IndexOf(local);

                compiler.CurrentFunction.Body.Emit(
                    Opcode.Move,
                    new Operand
                    {
                        Type = OperandType.Local,
                        Value = idx,
                        Size = local.Type.Size
                    },
                    compiler.AssignmentRHS.Operand
                );
                return true;
            }

            return false;
        }

        internal sealed record Parameter : CobVariable
        {
            public bool IsSpread { get; }

            public Parameter(string name, CobType type, bool isSpread)
                : base(name, type, true)
            {
                IsSpread = isSpread;
            }
        }

        public bool IsVisibleTo(IContext context) => Compiler.StandardIsSymbolVisibleHeuristic(context, Parent, Name);
    }

    internal enum CallingConvention
    {
        None,
        CCall,
        StdCall,
        ThisCall
    }

    internal sealed record Storage(Function Parent, Operand Operand, CobType Type)
    {
        public void Free()
        {
            Parent?.FreeStorage(Operand);
        }
    }
}