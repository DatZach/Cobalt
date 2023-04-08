﻿using System.Runtime.InteropServices;
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
        
        public void ExecuteFunction(Function function)
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
                        var format = localStack.Pop();
                        var formatVar = compiler.Globals[(int)format].Data;
                        var formatString = Encoding.UTF8.GetString(formatVar);
                        var a = localStack.Pop();

                        // TODO Support native and bytecode calls
                        var sub = nativeLibraries["msvcrt"].Functions["printf"];
                        sub(formatString, (int)a);
                        //var sub = compiler.Functions[(int)inst.A!.Value];
                        //ExecuteFunction(sub);
                        break;
                    }
                    case Opcode.Return:
                        functionStack.Pop();
                        return;
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

            // TODO Uncomment
            //throw new InvalidOperationException("End of buffer without a ret instruction");
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
                    throw new NotImplementedException();
                default:
                    throw new ArgumentOutOfRangeException();
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
