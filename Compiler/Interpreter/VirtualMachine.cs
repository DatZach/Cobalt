using Compiler.CodeGeneration;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using OperandType = Compiler.CodeGeneration.OperandType;

namespace Compiler.Interpreter
{
    internal sealed class VirtualMachine : IDisposable
    {
        // TODO Remove function and parameter stacks, poor engineering
        private Function currentFunction => functionStack.Peek(); // TODO Optimize
        private IReadOnlyList<CobVariable>? currentParameters => parameterStack.Peek(); // TODO Optimize

        private bool cmpResult;

        private readonly Stack<Function> functionStack;
        private readonly Stack<IReadOnlyList<CobVariable>?> parameterStack;
        private readonly Stack<CobVariable> localStack;
        private readonly CobVariable[] registers;

        private readonly NativeLibrariesProxy nativeLibrariesProxy;
        private readonly CodeGeneration.Compiler compiler;

        public VirtualMachine(CodeGeneration.Compiler compiler)
        {
            this.compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
            functionStack = new Stack<Function>(4);
            parameterStack = new Stack<IReadOnlyList<CobVariable>?>(4);
            localStack = new Stack<CobVariable>(4);
            registers = new CobVariable[64];
            
            nativeLibrariesProxy = NativeLibrariesProxy.FromCompiler(compiler);
        }
        
        public CobVariable? ExecuteFunction(Function function, IReadOnlyList<CobVariable>? parameters = null)
        {
            functionStack.Push(function);
            parameterStack.Push(parameters);

            var instructions = function.Body.Instructions;
            for (int i = 0; i < instructions.Count; ++i)
            {
                var inst = instructions[i];
                switch (inst.Opcode)
                {
                    case Opcode.None:
                        break;
                    case Opcode.Call:
                    {
                        var callee = ReadOperandAsFunction(inst.A!);
                        IReadOnlyList<CobVariable>? calleeParameters;
                        if (callee.Parameters.Count > 0)
                        {
                            var aCalleeParameters = new CobVariable[callee.Parameters.Count];
                            for (int j = 0; j < aCalleeParameters.Length; ++j)
                                aCalleeParameters[j] = ReadOperand(inst.C![j]);

                            calleeParameters = aCalleeParameters;
                        }
                        else
                            calleeParameters = null;

                        var native = callee.NativeImport;
                        var result = native != null
                            ? nativeLibrariesProxy.Invoke(native, calleeParameters)
                            : ExecuteFunction(callee, calleeParameters);

                        if (result != null)
                            WriteOperand(Operand.R0, result);
                        break;
                    }
                    case Opcode.Return:
                    {
                        var value = inst.A != null ? ReadOperand(inst.A) : null;
                        parameterStack.Pop();
                        functionStack.Pop();
                        return value;
                    }
                    case Opcode.Move:
                    {
                        WriteOperand(inst.A!, ReadOperand(inst.B!));
                        break;
                    }
                    case Opcode.GetField:
                    {
                        CobVariable? fieldValue;
                        var obj = ReadOperand(inst.C![0]);
                        var fieldIdx = inst.C![1].Value;
                        if (fieldIdx == 0)
                        {
                            // TODO Strings are actually Structs, when implemented in the language this hack can be fixed
                            if (obj.Value is byte[])
                                fieldValue = new CobVariable("$imm", CobType.U64, false, obj.BufferValue.Length);
                            else
                                fieldValue = obj.StructValue[0];
                        }
                        else
                            fieldValue = obj.StructValue[fieldIdx];

                        WriteOperand(inst.A!, fieldValue);
                        break;
                    }
                    case Opcode.SetField:
                    {
                        var obj = ReadOperand(inst.A!);
                        var fieldIdx = inst.C![0].Value;
                        var value = ReadOperand(inst.C![1]);
                        obj.StructValue[fieldIdx] = value;
                        break;
                    }
                    case Opcode.Push:
                    {
                        localStack.Push(ReadOperand(inst.A!));
                        break;
                    }
                    case Opcode.Pop:
                    {
                        WriteOperand(inst.A!, localStack.Pop());
                        break;
                    }
                    case Opcode.BitShr:
                    {
                        var a = ReadOperand(inst.A!).IntValue;
                        a >>= (int)ReadOperand(inst.B!).IntValue;
                        WriteOperand(inst.A!, a.ToCobVariable());
                        break;
                    }
                    case Opcode.BitShl:
                    {
                        var a = ReadOperand(inst.A!).IntValue;
                        a <<= (int)ReadOperand(inst.B!).IntValue;
                        WriteOperand(inst.A!, a.ToCobVariable());
                        break;
                    }
                    case Opcode.BitAnd:
                    {
                        var a = ReadOperand(inst.A!).IntValue;
                        a &= ReadOperand(inst.B!).IntValue;
                        WriteOperand(inst.A!, a.ToCobVariable());
                        break;
                    }
                    case Opcode.BitXor:
                    {
                        var a = ReadOperand(inst.A!).IntValue;
                        a ^= ReadOperand(inst.B!).IntValue;
                        WriteOperand(inst.A!, a.ToCobVariable());
                        break;
                    }
                    case Opcode.BitOr:
                    {
                        var a = ReadOperand(inst.A!).IntValue;
                        a |= ReadOperand(inst.B!).IntValue;
                        WriteOperand(inst.A!, a.ToCobVariable());
                        break;
                    }
                    case Opcode.Add:
                    {
                        var a = ReadOperand(inst.A!).IntValue;
                        a += ReadOperand(inst.B!).IntValue;
                        WriteOperand(inst.A!, a.ToCobVariable());
                        break;
                    }
                    case Opcode.Sub:
                    {
                        var a = ReadOperand(inst.A!).IntValue;
                        a -= ReadOperand(inst.B!).IntValue;
                        WriteOperand(inst.A!, a.ToCobVariable());
                        break;
                    }
                    case Opcode.Mul:
                    {
                        var a = ReadOperand(inst.A!).IntValue;
                        a *= ReadOperand(inst.B!).IntValue;
                        WriteOperand(inst.A!, a.ToCobVariable());
                        break;
                    }
                    case Opcode.Div:
                    {
                        var a = ReadOperand(inst.A!).IntValue;
                        a /= ReadOperand(inst.B!).IntValue;
                        WriteOperand(inst.A!, a.ToCobVariable());
                        break;
                    }
                    case Opcode.Mod:
                    {
                        var a = ReadOperand(inst.A!).IntValue;
                        a %= ReadOperand(inst.B!).IntValue;
                        WriteOperand(inst.A!, a.ToCobVariable());
                        break;
                    }
                    case Opcode.Compare:
                    {
                        // TODO Proper implementation supporting all types
                        var a = ReadOperand(inst.A!).IntValue;
                        var b = ReadOperand(inst.B!).IntValue;
                        cmpResult = a == b;
                        break;
                    }
                    case Opcode.JumpIfFalse:
                    {
                        if (!cmpResult)
                            i = currentFunction.Body.Labels[(int)inst.A!.Value].Location - 1;
                        break;
                    }
                    case Opcode.Jump:
                    {
                        i = currentFunction.Body.Labels[(int)inst.A!.Value].Location - 1;
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            throw new InvalidOperationException("End of buffer without a ret instruction");
        }

        private void WriteOperand(Operand operand, CobVariable value)
        {
            switch (operand.Type)
            {
                case OperandType.ImmediateSigned:
                case OperandType.ImmediateUnsigned:
                case OperandType.ImmediateFloat:
                    throw new InvalidOperationException();
                case OperandType.Register:
                    registers[operand.Value] = value;
                    break;
                case OperandType.Local:
                    currentFunction.Locals[(int)operand.Value] = value;
                    break;
                case OperandType.Global:
                    compiler.Globals[(int)operand.Value] = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private CobVariable ReadOperand(Operand operand)
        {
            switch (operand.Type)
            {
                case OperandType.ImmediateSigned:
                    return new CobVariable("$imm", CobType.Int, false, operand.Value);
                case OperandType.ImmediateUnsigned:
                    return new CobVariable("$imm", CobType.UInt, false, operand.Value);
                case OperandType.ImmediateFloat:
                    return new CobVariable("$imm", CobType.Float, false, operand.Value);
                case OperandType.Register:
                    return registers[operand.Value];
                case OperandType.Argument:
                    return currentParameters[(int)operand.Value];
                case OperandType.Local:
                    return currentFunction.Locals[(int)operand.Value];
                case OperandType.Global:
                    return compiler.Globals[(int)operand.Value];
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        // TODO Deprecate?
        // TODO This really should be unified somehow, but this is the fastest approach rn
        private Function ReadOperandAsFunction(Operand operand)
        {
            switch (operand.Type)
            {
                // TODO AAAA???? This is just as hacky! We don't know if we have a global or a function index or what
                case OperandType.Register:
                {
                    var global = compiler.Globals[(int)registers[operand.Value].IntValue];
                    if (global.Type == eCobType.Function)
                        return global.Type.Function;

                    throw new InvalidOperationException($"VM Expected function but received {global.Type} instead");
                }

                case OperandType.Global:
                {
                    var global = compiler.Globals[(int)operand.Value];
                    if (global.Type == eCobType.Function)
                        return global.Type.Function;

                    throw new InvalidOperationException($"VM Expected function but received {global.Type} instead");
                }

                default:
                    throw new InvalidOperationException($"VM Expected function operand but received {operand.Type} instead");
            }
        }

        public void Dispose()
        {
            nativeLibrariesProxy.Dispose();
        }

        public sealed class NativeLibrariesProxy : IDisposable
        {
            public delegate int NativeWrapperDelegate(CobVariable[]? variables);

            private readonly Dictionary<string, NativeLibraryProxy> proxies;

            private NativeLibrariesProxy(Dictionary<string, NativeLibraryProxy> proxies)
            {
                this.proxies = proxies;
            }

            public CobVariable? Invoke(Import native, IReadOnlyList<CobVariable>? parameters)
            {
                if (native.Function == null)
                    return null;

                int result;
                var proxy = proxies[native.Library];
                var methodDelegate = proxy.Functions[native.SymbolName!];
                if (native.SymbolName == "printf") // HACK!!! REMOVE WHEN FORMATTING IS NATIVE
                {
                    Console.Write(
                        Encoding.UTF8.GetString(parameters[0].BufferValue),
                        parameters.Skip(1).Select(x => x.Value).ToArray()
                    );
                    result = 0;
                }
                else
                    result = methodDelegate(parameters?.ToArray());

                return new CobVariable("$imm", CobType.U64, false, result); // TODO Proxy more types than integers
            }

            public static NativeLibrariesProxy FromCompiler(CodeGeneration.Compiler compiler)
            {
                var proxies = new Dictionary<string, NativeLibraryProxy>();

                foreach (var import in compiler.Imports)
                {
                    if (!proxies.TryGetValue(import.Library, out var proxy))
                    {
                        var nativeLibrary = NativeLibrary.Load(import.Library);
                        proxy = new NativeLibraryProxy(nativeLibrary);
                        proxies.Add(import.Library, proxy);
                    }

                    var address = NativeLibrary.GetExport(proxy.Library, import.SymbolName!);

                    var method = new System.Reflection.Emit.DynamicMethod(
                        $"dynm_{import.SymbolName}",
                        typeof(int),
                        new [] { typeof(CobVariable[]) },
                        typeof(NativeLibrariesProxy).Module
                    );

                    method.DefineParameter(0, ParameterAttributes.In, "variables");

                    var il = method.GetILGenerator();
                    for (int i = 0; i < import.Function!.Parameters.Count; ++i)
                    {
                        il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
                        il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4, i);
                        il.Emit(System.Reflection.Emit.OpCodes.Ldelem_Ref);
                        if (import.Function.Parameters[i].Type == CobType.String)
                        {
                            il.Emit(System.Reflection.Emit.OpCodes.Callvirt, typeof(CobVariable).GetProperty(nameof(CobVariable.BufferValue))!.GetGetMethod()!);
                            il.Emit(System.Reflection.Emit.OpCodes.Call, typeof(NativeLibrariesProxy).GetMethod(nameof(NativeLibrariesProxy.GetString))!);
                        }
                        else
                            il.Emit(System.Reflection.Emit.OpCodes.Callvirt, typeof(CobVariable).GetProperty(nameof(CobVariable.IntValue))!.GetGetMethod()!);
                    }

                    il.Emit(System.Reflection.Emit.OpCodes.Ldc_I8, address.ToInt64());
                    il.EmitCalli(
                        System.Reflection.Emit.OpCodes.Calli, System.Runtime.InteropServices.CallingConvention.Cdecl,
                        import.Function.ReturnType.ToManagedType(),
                        import.Function.Parameters.Select(x => x.Type.ToManagedType()).ToArray()
                    );

                    il.Emit(System.Reflection.Emit.OpCodes.Ret);

                    var methodDelegate = method.CreateDelegate<NativeWrapperDelegate>();
                    proxy.Functions.Add(import.SymbolName!, methodDelegate);
                }

                return new NativeLibrariesProxy(proxies);
            }

            public void Dispose()
            {
                foreach (var proxy in proxies.Values)
                    NativeLibrary.Free(proxy.Library);

                proxies.Clear();
            }

            private sealed class NativeLibraryProxy
            {
                public IntPtr Library { get; }

                public Dictionary<string, NativeWrapperDelegate> Functions { get; }

                public NativeLibraryProxy(IntPtr library)
                {
                    Library = library;
                    Functions = new Dictionary<string, NativeWrapperDelegate>();
                }
            }

            public static string GetString(byte[] data) => Encoding.UTF8.GetString(data);
        }
    }

    internal static class CobVariableExtensions
    {
        public static CobVariable ToCobVariable(this long value)
        {
            return new CobVariable("$imm", CobType.U64, false, value);
        }
    }
}
