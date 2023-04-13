using System;
using System.Runtime.InteropServices;
using System.Text;
using Compiler.CodeGeneration;

namespace Compiler.Interpreter
{
    internal sealed class VirtualMachine : IDisposable
    {
        private Function currentFunction => functionStack.Peek(); // TODO Optimize
        private readonly Dictionary<string, LoadedNativeLibrary> nativeLibraries;
        private readonly Stack<Function> functionStack;
        private readonly Stack<long> localStack;
        private readonly long[] registers;

        private readonly CodeGeneration.Compiler compiler;

        private delegate int PrintfDelegate(string format, int a);

        public VirtualMachine(CodeGeneration.Compiler compiler)
        {
            this.compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
            functionStack = new Stack<Function>(4);
            localStack = new Stack<long>(4);
            registers = new long[64];
            
            nativeLibraries = new Dictionary<string, LoadedNativeLibrary>();

            // TODO Doing too much in the ctor
            var libraries = compiler.Imports.Where(x => x.SymbolName != null)
                .Select(x => x.Library)
                .Distinct()
                .ToList();
            foreach (var library in libraries)
            {
                var functionNames = compiler.Imports.Where(x => x.Library == library)
                                                    .Select(x => x.SymbolName)
                                                    .ToList();
                var nativeLibrary = new LoadedNativeLibrary(library, functionNames);
                nativeLibraries.Add(library, nativeLibrary);
            }
        }
        
        public long ExecuteFunction(Function function)
        {
            functionStack.Push(function);

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
                        var callee = ReadFunctionOperand(inst.A!);
                        var native = callee.NativeImport;
                        if (native != null)
                        {
                            // TODO Get rid of the prototype hardcoded hacks
                            var format = localStack.Pop();
                            var formatVar = compiler.Globals[(int)format].Data;
                            var formatString = Encoding.UTF8.GetString(formatVar);
                            var a = localStack.Pop();
                            
                            var sub = nativeLibraries[native.Library].Functions[native.SymbolName!];
                            sub(formatString, (int)a);
                        }
                        else
                        {
                            ExecuteFunction(callee);
                        }
                        break;
                    }
                    case Opcode.Return:
                    {
                        long value;
                        if (inst.A != null)
                            value = ReadOperand(inst.A);
                        else
                            value = -1;

                        functionStack.Pop();
                        return value;
                    }
                    case Opcode.RestoreStack:
                    {
                        var size = ReadOperand(inst.A!) / 4;
                        while (size-- > 0)
                            localStack.Pop();
                        break;
                    }
                    case Opcode.Move:
                    {
                        WriteOperand(inst.A!, ReadOperand(inst.B!));
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
                        var a = ReadOperand(inst.A!);
                        a >>= (int)ReadOperand(inst.B!);
                        WriteOperand(inst.A!, a);
                        break;
                    }
                    case Opcode.BitShl:
                    {
                        var a = ReadOperand(inst.A!);
                        a <<= (int)ReadOperand(inst.B!);
                        WriteOperand(inst.A!, a);
                        break;
                    }
                    case Opcode.BitAnd:
                    {
                        var a = ReadOperand(inst.A!);
                        a &= ReadOperand(inst.B!);
                        WriteOperand(inst.A!, a);
                        break;
                    }
                    case Opcode.BitXor:
                    {
                        var a = ReadOperand(inst.A!);
                        a ^= ReadOperand(inst.B!);
                        WriteOperand(inst.A!, a);
                        break;
                    }
                    case Opcode.BitOr:
                    {
                        var a = ReadOperand(inst.A!);
                        a |= ReadOperand(inst.B!);
                        WriteOperand(inst.A!, a);
                        break;
                    }
                    case Opcode.Add:
                    {
                        var a = ReadOperand(inst.A!);
                        a += ReadOperand(inst.B!);
                        WriteOperand(inst.A!, a);
                        break;
                    }
                    case Opcode.Sub:
                    {
                        var a = ReadOperand(inst.A!);
                        a -= ReadOperand(inst.B!);
                        WriteOperand(inst.A!, a);
                        break;
                    }
                    case Opcode.Mul:
                    {
                        var a = ReadOperand(inst.A!);
                        a *= ReadOperand(inst.B!);
                        WriteOperand(inst.A!, a);
                        break;
                    }
                    case Opcode.Div:
                    {
                        var a = ReadOperand(inst.A!);
                        a /= ReadOperand(inst.B!);
                        WriteOperand(inst.A!, a);
                        break;
                    }
                    case Opcode.Mod:
                    {
                        var a = ReadOperand(inst.A!);
                        a %= ReadOperand(inst.B!);
                        WriteOperand(inst.A!, a);
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            throw new InvalidOperationException("End of buffer without a ret instruction");
        }

        private void WriteOperand(Operand operand, long value)
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
                case OperandType.Pointer:
                    throw new InvalidOperationException();
                case OperandType.Local:
                    currentFunction.Locals[(int)operand.Value].Value = value;
                    break;
                case OperandType.Global:
                    throw new InvalidOperationException();
                case OperandType.Function:
                    throw new InvalidOperationException();
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private long ReadOperand(Operand operand)
        {
            switch (operand.Type)
            {
                case OperandType.ImmediateSigned:
                case OperandType.ImmediateUnsigned:
                case OperandType.ImmediateFloat:
                    return operand.Value;
                case OperandType.Register:
                    return registers[operand.Value];
                case OperandType.Pointer:
                    throw new NotImplementedException();
                case OperandType.Local:
                    return currentFunction.Locals[(int)operand.Value].Value;
                case OperandType.Global:
                    // TODO Probably a garbage implementation
                    return operand.Value;
                    //throw new NotImplementedException();
                case OperandType.Function:
                    return operand.Value; // TODO Hacky??
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        // TODO This really should be unified somehow, but this is the fastest approach rn
        private Function ReadFunctionOperand(Operand operand)
        {
            switch (operand.Type)
            {
                // TODO AAAA???? This is just as hacky! We don't know if we have a global or a function index or what
                case OperandType.Register:
                {
                    var global = compiler.Globals[(int)registers[operand.Value]];
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

                case OperandType.Function:
                    return compiler.Functions[(int)operand.Value];

                default:
                    throw new InvalidOperationException($"VM Expected function operand but received {operand.Type} instead");
            }
        }
        
        public void Dispose()
        {
            foreach (var library in nativeLibraries.Values)
                library.Dispose();
            nativeLibraries.Clear();
        }

        private sealed record LoadedNativeLibrary : IDisposable
        {
            public IntPtr Library { get; }

            public Dictionary<string, PrintfDelegate> Functions { get; }

            public LoadedNativeLibrary(string path, IReadOnlyList<string> functionNames)
            {
                Library = NativeLibrary.Load(path);
                Functions = new Dictionary<string, PrintfDelegate>();
                foreach (var functionName in functionNames)
                {
                    // TODO Generic delegates
                    var address = NativeLibrary.GetExport(Library, functionName);
                    var function = Marshal.GetDelegateForFunctionPointer<PrintfDelegate>(address);
                    Functions.Add(functionName, function);
                }
            }

            public void Dispose()
            {
                Functions.Clear();
                NativeLibrary.Free(Library);
            }
        }
    }
}
