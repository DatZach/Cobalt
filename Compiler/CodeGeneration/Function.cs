using Compiler.Ast.Expressions;

namespace Compiler.CodeGeneration
{
    internal sealed class Function
    {
        public string Name { get; }

        public List<CobVariable> Locals { get; }

        public uint ClobberedRegisters { get; private set; }

        public CallingConvention CallingConvention { get; }

        public Import? NativeImport { get; set; } // TODO init??

        public IReadOnlyList<Parameter> Parameters { get; }

        public CobType ReturnType { get; set; }

        public InstructionBuffer Body { get; }

        public Label ReturnLabel { get; }

        private int registerStack;

        public Function(
            string name,
            CallingConvention callingConvention,
            IReadOnlyList<Parameter> parameters,
            CobType returnType
        )
        {
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
            ClobberedRegisters |= 1u << register;

            return register;
        }

        public int FreeRegister()
        {
            return --registerStack;
        }

        public int PeekRegister()
        {
            return registerStack - 1;
        }

        public int PreserveRegister(int register)
        {
            if (register < registerStack)
            {
                Body.EmitR(Opcode.Stash, register);
                return register;
            }

            return -1;
        }

        public void RestoreRegister(int register)
        {
            if (register == -1)
                return;

            Body.EmitR(Opcode.Unstash, register);
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

        public int FindParameter(string name)
        {
            for (int i = 0; i < Parameters.Count; ++i)
            {
                if (Parameters[i].Name == name)
                    return i;
            }

            return -1;
        }

        public int ResolveStackSpaceRequired()
        {
            int bytes = 0;
            for (var i = 0; i < Locals.Count; ++i)
                bytes += (Locals[i].Type.Size + 7) / 8;

            return bytes;
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
        Stdcall
    }
}