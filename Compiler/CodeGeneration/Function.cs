using Compiler.Ast.Expressions;
using System.Diagnostics;

namespace Compiler.CodeGeneration
{
    [DebuggerDisplay("Function '{FullyQualifiedName}'")]
    internal sealed class Function : IContext
    {
        public string Name { get; }

        public Module Module { get; }

        public List<CobVariable> Locals { get; }

        public uint ClobberedRegisters { get; private set; }

        public CallingConvention CallingConvention { get; }

        public Import? NativeImport { get; set; } // TODO init??

        public IReadOnlyList<Parameter> Parameters { get; }

        public CobType ReturnType { get; set; }

        public InstructionBuffer Body { get; }

        public Label ReturnLabel { get; }

        public string FullyQualifiedName => (Module.Name ?? "root") + '_' + Name;

        private int freeRegisterIndex;
        private int registers;

        public Function(
            string name,
            Module module,
            CallingConvention callingConvention,
            IReadOnlyList<Parameter> parameters,
            CobType returnType
        )
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Module = module ?? throw new ArgumentNullException(nameof(module));
            Locals = new List<CobVariable>();
            ClobberedRegisters = 0;
            CallingConvention = callingConvention;
            Parameters = parameters;
            ReturnType = returnType;
            Body = new InstructionBuffer();
            ReturnLabel = Body.AllocateLabel();

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
                eCobType.Tuple => AllocateLocal(new CobVariable("$tuple", type, true)),
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
            const int MaxRegisters = 32;

            var i = freeRegisterIndex;
            while ((registers & (1 << i)) != 0 && i < MaxRegisters)
                ++i;

            if (i >= MaxRegisters)
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
        
        public Storage? GetIdentifier(Compiler compiler, IdentifierExpression expression)
        {
            var value = expression.Value;
            int idx;

            // TODO AllocateStorage(Type, Value, Origin)..?

            // ARGUMENTS
            if ((idx = FindParameter(value)) != -1)
            {
                var type = Parameters[idx];
                return new Storage(
                    this,
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
            if ((idx = FindLocal(value)) != -1)
            {
                var type = Locals[idx];
                return new Storage(
                    this,
                    new Operand
                    {
                        Type = OperandType.Local,
                        Value = idx,
                        Size = type.Type.Size
                    },
                    type.Type
                );
            }

            // Find in parent scope
            return compiler.ParentContext.GetIdentifier(compiler, expression);
        }

        public void SetIdentifier(Compiler compiler, IdentifierExpression expression)
        {
            var value = expression.Value;
            int idx;

            // ARGUMENTS
            if ((idx = FindParameter(value)) != -1)
            {
                var parameter = Parameters[idx];

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
                return;
            }

            // LOCALS
            if ((idx = FindLocal(value)) != -1)
            {
                var local = Locals[idx];

                compiler.ValidateVariableAccess(local, expression);
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
                return;
            }

            // Parent Scope
            compiler.ParentContext.SetIdentifier(compiler, expression);
        }

        public sealed class Parameter
        {
            public string Name { get; }

            public CobType Type { get; }

            public bool IsSpread { get; }

            public Parameter(string name, CobType type, bool isSpread)
            {
                Name = name;
                Type = type;
                IsSpread = isSpread;
            }
        }
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