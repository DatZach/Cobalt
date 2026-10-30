using System.Diagnostics;
using System.Text;
using Compiler.Ast;
using Compiler.Ast.Expressions;
using Compiler.Interpreter;
using Compiler.Lexer;

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

        public HashSet<ISymbol> MutatedSymbols { get; }

        public Token? Operator { get; set; }

        public string FullyQualifiedName => Parent.Name + '_' + Name;

        public BlockScope CurrentBlockScope => blockScopeStack.Peek();

        public bool IsCasting => ReturnType.Name == Name;

        public bool IsSignature { get; }

        public bool IsConstructor { get; init; } // TODO Maybe this should be implemented as OOP, not sure

        public bool IsDestructor { get; init; } // TODO Yeah definitely needs to be refactored somehow...

        private int freeRegisterIndex;
        private int registers;

        private readonly Stack<BlockScope> blockScopeStack;
        private readonly List<Variable> locals;
        private readonly Compiler compiler;

        public Function(
            string name,
            IScopeContext parent,
            Compiler compiler,
            CallingConvention callingConvention,
            IReadOnlyList<Parameter> parameters,
            CobType returnType,
            bool isSignature
        ) {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Parent = parent ?? throw new ArgumentNullException(nameof(parent));
            ClobberedRegisters = 0;
            CallingConvention = callingConvention;
            Parameters = parameters;
            ReturnType = returnType;
            IsSignature = isSignature;
            Body = new InstructionBuffer();
            MutatedSymbols = new HashSet<ISymbol>();

            blockScopeStack = new Stack<BlockScope>();
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

        public BlockScope PushBlockScope()
        {
            var blockScope = new BlockScope(this);
            blockScopeStack.Push(blockScope);
            return blockScope;
        }

        public BlockScope PopBlockScope()
        {
            var scope = blockScopeStack.Peek();

            foreach (var expr in scope.DeferExpressions)
                expr.Accept(compiler);

            scope.EnsureLinearOwnership();
            blockScopeStack.Pop();

            return scope;
        }

        public Variable AllocateLocal(string name, CobType type, bool mutable)
        {
            var local = new Variable(name, type, mutable);
            locals.Add(local);

            blockScopeStack.Peek().AddLocal(local);

            return local;
        }

        public Variable? FindLocal(string name)
        {
            foreach (var scope in blockScopeStack)
            {
                var symbol = scope.FindLocal(name);
                if (symbol != null)
                    return symbol;
            }


            return null;
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

        CobType ISymbol.Type => CobType.Function;

        bool ISymbol.Mutable => false;

        public bool IsVisibleTo(IScopeContext context) => Compiler.IsSymbolVisible(context, Parent, Name);

        //public override string ToString() => FullyQualifiedName;
        public override string ToString()
        {
            var sb = new StringBuilder(80);
            sb.Append("function "); // TODO ??
            sb.Append(Name);
            sb.Append('(');
            for (var i = 0; i < Parameters.Count; ++i)
            {
                var x = Parameters[i];
                if (x.IsSpread) sb.Append("...");
                sb.Append(x.Type);
                sb.Append(' ');
                sb.Append(x.Name);
                
                if (x.DefaultValue != null)
                    sb.Append(" = ?");
                if (i < Parameters.Count - 1)
                    sb.Append(", ");
            }

            sb.Append(')');
            if (ReturnType != eCobType.None)
            {
                sb.Append(' ');
                sb.Append(ReturnType);
            }

            if (CallingConvention != CallingConvention.Default)
            {
                sb.Append(", ");
                sb.Append(CallingConvention);
            }

            return sb.ToString();
        }

        public static bool IsLegalOperatorAlias(Token token)
        {
            return token.Type is TokenType.Add or TokenType.Subtract or TokenType.Multiply or TokenType.Exponent
                              or TokenType.Divide or TokenType.DivideCeil or TokenType.DivideFloor
                              or TokenType.Remainder or TokenType.Modulo
                              or TokenType.BitOr or TokenType.BitAnd or TokenType.BitXor or TokenType.BitLeftShift
                              or TokenType.BitRightShift or TokenType.BitLeftRotate or TokenType.BitRightRotate
                              or TokenType.Sign
                              or TokenType.Equals or TokenType.NotEquals or TokenType.LessThanOrEquals
                              or TokenType.MoreThanOrEquals or TokenType.LessThan or TokenType.MoreThan;
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

        public sealed class BlockScope : IScopeContext
        {
            string IScopeContext.Name => "";

            IScopeContext IScopeContext.Parent => parent;

            public IReadOnlyList<Expression> DeferExpressions => deferExpressions;

            private readonly Dictionary<string, Variable> locals;
            private readonly List<Expression> deferExpressions;
            private readonly List<NarrowingDirective> narrows;
            private readonly Function parent;

            public BlockScope(Function parent)
            {
                this.parent = parent;

                locals = new Dictionary<string, Variable>();
                deferExpressions = new List<Expression>();
                narrows = new List<NarrowingDirective>();
            }

            public void AddLocal(Variable local)
            {
                locals[local.Name] = local;
                local.Owner = this; // TODO Is this the right place for this?
            }
            
            public Variable? FindLocal(string name)
            {
                return locals.GetValueOrDefault(name);
            }

            public bool CommitNarrowingDirectives(bool invert)
            {
                foreach (var narrow in narrows)
                {
                    if (narrow.Committed)
                        continue;

                    var oldType = narrow.Variable.Type;
                    var newType = narrow.Type;
                    if (invert)
                        newType = oldType.Bust(newType);

                    if (newType == eCobType.None)
                        return false;

                    ((ILateTypeBinding)narrow.Variable).RebindType(newType);
                    narrow.Type = oldType;
                    narrow.Committed = true;
                }

                return true;
            }

            public void RevertNarrowingDirectives()
            {
                foreach (var narrow in narrows)
                {
                    if (!narrow.Committed)
                        continue;

                    var oldType = narrow.Variable.Type;
                    ((ILateTypeBinding)narrow.Variable).RebindType(narrow.Type);
                    narrow.Type = oldType;
                    narrow.Committed = false;
                }
            }

            public void AddNarrowingDirective(Operand operand, CobType type)
            {
                // TODO Support !::

                Variable? target;
                if (operand.Type == OperandType.Local)
                    target = parent.Locals[(int)operand.Value];
                else if (operand.Type == OperandType.Global)
                    target = parent.compiler.Globals[(int)operand.Value];
                else if (operand.Type == OperandType.Argument)
                    target = parent.Parameters[(int)operand.Value];
                else
                    throw new NotImplementedException();//return;

                narrows.Add(new NarrowingDirective(target, type));
            }

            public void AddDeferExpression(Expression expression)
            {
                deferExpressions.Insert(0, expression);
            }

            public bool EnsureLinearOwnership()
            {
                return false;
                foreach (var x in locals.Values)
                {
                    if (x.Owner == this && x.Type.TagRecord != null && x.Type.TagRecord.IsLinear)
                    {
                        parent.compiler.Messages.Add(Message.IllegalLinearTypeLifetimeExpiration, Token.EndOfStream, x.Name);
                    }
                }

                return false;
            }

            ISymbol? IScopeContext.FindSymbol(string name)
            {
                return FindLocal(name);
            }

            Storage? IScopeContext.EmitGetForSymbol(ISymbol symbol)
            {
                return parent.EmitGetForSymbol(symbol);
            }

            bool IScopeContext.EmitSetForSymbol(ISymbol symbol)
            {
                return false;
            }

            private sealed class NarrowingDirective
            {
                public Variable Variable { get; }

                public CobType Type { get; set; }


                public bool Committed { get; set; }

                public NarrowingDirective(Variable variable, CobType type)
                {
                    Variable = variable;
                    Type = type;
                    Committed = false;
                }
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

        public Function? ResolveSingle(IReadOnlyList<CobType>? arguments, CobType? returnType = null)
        {
            var list = candidates.Where(x =>
            {
                if (returnType != null && !CobType.IsExplicitCastable(x.ReturnType, returnType))
                    return false;

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

                    if (!CobType.IsExplicitCastable(b, a))
                        return false;
                }

                return true;
            }).ToList();
            return list.Count != 1 ? null : list[0];
        }

        CobType ISymbol.Type => CobType.Function;

        bool ISymbol.Mutable => false;

        public bool IsVisibleTo(IScopeContext context) => candidates.Any(x => x.IsVisibleTo(context));

        public override string ToString() => string.Join('\n', candidates);
    }

    // TODO Might be a neat refactor to implement IDisposable for calling Free()
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

        public Storage(Storage other, CobType type)
            : this(type, other.Operand, other.context)
        {
            // NOTE Ctor intended for explicit casts between sub/super types
        }

        public void Free()
        {
            context?.FreeStorage(Operand);
        }
    }
}