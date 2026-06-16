using System.Diagnostics;
using System.Text;
using Compiler.Ast.Expressions;
using Compiler.Interpreter;

namespace Compiler.CodeGeneration.Artifacts
{
    [DebuggerDisplay("Function '{FullyQualifiedName}'")]
    internal sealed class Function : IScopeContext, ISymbol
    {
        public string Name { get; }

        public IScopeContext Parent { get; }

        public IReadOnlyList<Variable> Locals => locals;

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

        private readonly List<Variable> locals;
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

            locals = new List<Variable>();

            this.compiler = compiler;

            freeRegisterIndex = 0;
            registers = 0;
        }

        //public Storage AllocateStorage(CobType type, Operand operand)
        //{
        //    return new Storage(operand, type, this);
        //}

        public Storage AllocateRegisterStorage(CobType type)
        {
            var register = AllocateRegister();
            var operand = Operand._Register(register);

            return new Storage(type, operand, this);
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

        public Variable AllocateLocal(string name, CobType type, bool mutable)
        {
            var local = new Variable(name, type, mutable);
            locals.Add(local);

            return local;
        }

        public Variable? FindLocal(string name)
        {
            return locals.FirstOrDefault(x => x.Name == name);
        }

        public int FindLocalIndex(Variable local)
        {
            return locals.IndexOf(local);
        }

        public Parameter? FindParameter(string name)
        {
            return Parameters.FirstOrDefault(x => x.Name == name);
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
            if ((parameter = FindParameter(name)) != null)
                return parameter;

            Variable? local;
            if ((local = FindLocal(name)) != null)
                return local;

            return null;
        }

        public Storage? EmitGetForSymbol(ISymbol symbol)
        {
            if (symbol is Parameter parameter)
            {
                var idx = FindParameterIndex(parameter);
                return new Storage(
                    parameter.Type,
                    Operand.Argument(idx)
                );
            }

            if (symbol is Variable variable)
            {
                var idx = locals.IndexOf(variable);
                return new Storage(
                    variable.Type,
                    Operand.Local(idx)
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
                    Operand.Argument(idx),
                    compiler.AssignmentRHS.Operand
                );
                return true;
            }

            // LOCALS
            if (symbol is Variable local)
            {
                var idx = locals.IndexOf(local);

                compiler.CurrentFunction.Body.Emit(
                    Opcode.Move,
                    Operand.Local(idx),
                    compiler.AssignmentRHS.Operand
                );
                return true;
            }

            return false;
        }

        public bool IsVisibleTo(IScopeContext context) => Compiler.IsSymbolVisible(context, Parent, Name);

        //public override string ToString() => FullyQualifiedName;
        public override string ToString()
        {
            var sb = new StringBuilder(80);
            sb.Append(Name);
            sb.Append('(');
            for (var i = 0; i < Parameters.Count; ++i)
            {
                var x = Parameters[i];
                if (x.IsSpread) sb.Append("...");
                sb.Append(x.Name);
                sb.Append(": ");
                sb.Append(x.Type);
                if (x.DefaultValue != null)
                    sb.Append(" = ?");
                if (i < Parameters.Count - 1)
                    sb.Append(", ");
            }

            sb.Append(')');
            if (ReturnType != eCobType.None)
            {
                sb.Append(": ");
                sb.Append(ReturnType);
            }

            return sb.ToString();
        }

        internal sealed record Parameter : Variable
        {
            public bool IsSpread { get; }

            public Expression? DefaultValue { get; }

            public Parameter(string name, CobType type, bool isSpread, Expression? defaultValue = null)
                : base(name, type, true)
            {
                IsSpread = isSpread;
                DefaultValue = defaultValue;
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

    internal sealed class FunctionCandidates : ISymbol
    {
        private readonly IReadOnlyList<Function> candidates;

        public FunctionCandidates(IReadOnlyList<Function> candidates)
        {
            this.candidates = candidates;
        }

        public Function? ResolveSingle(IReadOnlyList<CobType>? arguments)
        {
            return candidates.SingleOrDefault(x =>
            {
                if (arguments == null)
                    return true;

                var parameterCount = x.Parameters.Count;
                var parametersCountRequired = x.Parameters.Count(y => y.DefaultValue == null);
                var hasSpreadParameter = parameterCount > 0 && x.Parameters[^1].IsSpread;

                var argumentCount = arguments.Count;
                var paramOffset = 0;

                if (x.CallingConvention == CallingConvention.ThisCall)
                    ++paramOffset;

                if (parameterCount < argumentCount + paramOffset && !hasSpreadParameter)
                    return false;
                if (argumentCount + paramOffset < parametersCountRequired)
                    return false;

                for (int i = 0; i < argumentCount; ++i)
                {
                    var a = arguments[i];
                    CobType b;
                    if (hasSpreadParameter && i >= parameterCount - 1)
                    {
                        // Spread argument
                        b = x.Parameters[parameterCount - 1].Type;
                        b = CobType.Any; // TODO Need a way to get the ElementType from Array intrinsic
                    }
                    else
                        b = x.Parameters[i + paramOffset].Type;

                    if (!CobType.IsCastable(a, b))
                        return false;
                }

                return true;
            });
        }

        public bool IsVisibleTo(IScopeContext context) => candidates.Any(x => x.IsVisibleTo(context));

        public override string ToString() => string.Join('\n', candidates);
    }

    internal sealed record Storage
    {
        public CobType Type { get; }

        public Operand Operand { get; }

        private readonly Function? context;

        public Storage(CobType type, Operand operand, Function? context)
        {
            Type = type;
            Operand = operand;
            this.context = context;
        }

        public Storage(CobType type, Operand operand)
            : this(type, operand, null)
        {

        }

        public void Free()
        {
            context?.FreeStorage(Operand);
        }
    }
}