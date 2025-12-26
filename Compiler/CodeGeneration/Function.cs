using Compiler.Interpreter;
using System.Diagnostics;

namespace Compiler.CodeGeneration
{
    [DebuggerDisplay("Function '{FullyQualifiedName}'")]
    internal sealed class Function : IScopeContext, ISymbol
    {
        public string Name { get; }

        public IScopeContext Parent { get; }

        public IReadOnlyList<CobVariable> Locals => locals;

        public uint ClobberedRegisters { get; private set; }

        public CallingConvention CallingConvention { get; }

        public Import? NativeImport { get; set; } // TODO init??

        public IReadOnlyList<Parameter> Parameters { get; }

        public CobType ReturnType { get; set; }

        public InstructionBuffer Body { get; }

        public Label ReturnLabel { get; }

        public string FullyQualifiedName => Parent.Name + '_' + Name;

        private int freeRegisterIndex;
        private int registers;

        private readonly List<CobVariable> locals;
        private readonly Compiler compiler;

        public Function(
            string name,
            IScopeContext parent,
            Compiler compiler,
            CallingConvention callingConvention,
            IReadOnlyList<Parameter> parameters,
            CobType returnType
        ) {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Parent = parent ?? throw new ArgumentNullException(nameof(parent));
            ClobberedRegisters = 0;
            CallingConvention = callingConvention;
            Parameters = parameters;
            ReturnType = returnType;
            Body = new InstructionBuffer();
            ReturnLabel = Body.AllocateLabel();

            locals = new List<CobVariable>();

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

        public CobVariable AllocateLocal(string name, CobType type, bool mutable)
        {
            var local = new CobVariable(name, type, mutable);
            locals.Add(local);

            return local;
        }

        // TODO Deprecate
        public CobVariable AllocateLocal(CobVariable local)
        {
            locals.Add(local);
            return local;
        }

        public CobVariable? FindLocal(string name)
        {
            return locals.FirstOrDefault(x => x.Name == name);
        }

        public int FindLocalIndex(CobVariable local)
        {
            return locals.IndexOf(local);
        }

        public int FindParameterIndex(Parameter parameter)
        {
            for (int i = 0; i < Parameters.Count; ++i)
            {
                if (Parameters[i] == parameter)
                    return i;
            }

            return -1;
        }

        public ISymbol? FindSymbol(string name)
        {
            Parameter? parameter;
            if ((parameter = Parameters.FirstOrDefault(x => x.Name == name)) != null)
                return parameter;

            CobVariable? local;
            if ((local = Locals.FirstOrDefault(x => x.Name == name)) != null)
                return local;

            return null;
        }

        public Storage? EmitGetForSymbol(ISymbol symbol)
        {
            if (symbol is Parameter parameter)
            {
                var idx = FindParameterIndex(parameter);
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

            if (symbol is CobVariable variable)
            {
                var idx = locals.IndexOf(variable);
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

        public bool EmitSetForSymbol(ISymbol symbol)
        {
            // ARGUMENTS
            if (symbol is Parameter parameter)
            {
                var idx = FindParameterIndex(parameter);

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
            if (symbol is CobVariable local)
            {
                var idx = locals.IndexOf(local);

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

        public bool IsVisibleTo(IScopeContext context) => Compiler.IsSymbolVisible(context, Parent, Name);

        internal sealed record Parameter : CobVariable
        {
            public bool IsSpread { get; }

            public Parameter(string name, CobType type, bool isSpread)
                : base(name, type, true)
            {
                IsSpread = isSpread;
            }
        }
    }

    internal enum CallingConvention
    {
        None,
        CCall,
        StdCall,
        ThisCall,
        NakedCall,

        Default = CCall
    }

    internal sealed record Storage(Function Parent, Operand Operand, CobType Type)
    {
        public void Free()
        {
            Parent?.FreeStorage(Operand);
        }
    }
}