using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Compiler.CodeGeneration;
using Compiler.CodeGeneration.Artifacts;
using CallingConvention = Compiler.CodeGeneration.Artifacts.CallingConvention;

namespace Compiler.Interpreter
{
    internal sealed class VirtualMachine : IDisposable
    {
        public const int MaxRegisters = 32;

        private readonly NativeLibrariesProxy nativeLibrariesProxy;
        private readonly Artifact artifact;

        public VirtualMachine(Artifact artifact)
        {
            this.artifact = artifact ?? throw new ArgumentNullException(nameof(artifact));
            
            nativeLibrariesProxy = NativeLibrariesProxy.FromArtifact(artifact);
        }
        
        public Variable? ExecuteFunction(Function function, IList<Variable>? parameters = null)
        {
            var locals = new Variable[function.Locals.Count];
            var registers = new Variable[MaxRegisters];

            var instructions = function.Body.Instructions;
            for (int ip = 0; ip < instructions.Count; ++ip)
            {
                var inst = instructions[ip];
                switch (inst.Opcode)
                {
                    case Opcode.None:
                        break;
                    case Opcode.Call:
                    case Opcode.CallVirt:
                    {
                        Function callee;
                        if (inst.Opcode == Opcode.Call)
                            callee = ReadOperand(inst.A!).Type.TagFunction!;
                        else
                        {
                            var virtFunc = ReadOperand(inst.A!).Type.TagFunction!;
                            var @this = ReadOperand(inst.D![0]).Type.Tag;
                            if (@this is RecordType recordType)
                                callee = recordType.FindVirtualFunction(virtFunc)!;
                            else
                                throw new InvalidOperationException();
                        }

                        IList<Variable>? calleeParameters;
                        if (callee.Parameters.Count > 0)
                        {
                            var aCalleeParameters = new Variable[callee.Parameters.Count];
                            for (int j = 0; j < aCalleeParameters.Length; ++j)
                            {
                                var aCalleeParameter = ReadOperand(inst.D![j]);
                                if (j != 0 || callee.CallingConvention != CallingConvention.ThisCall)
                                    aCalleeParameter = aCalleeParameter.DeepClone();
                                
                                aCalleeParameters[j] = aCalleeParameter;
                            }

                            calleeParameters = aCalleeParameters;
                        }
                        else
                            calleeParameters = null;

                        var native = callee.NativeImport;
                        var result = native != null
                            ? nativeLibrariesProxy.Invoke(native, calleeParameters)
                            : ExecuteFunction(callee, calleeParameters);

                        if (callee.ReturnType != eCobType.None)
                            WriteOperand(inst.B!, result);
                        break;
                    }
                    case Opcode.Return:
                    {
                        var value = inst.A != null ? ReadOperand(inst.A) : null;
                        return value;
                    }
                    case Opcode.Move:
                    {
                        WriteOperand(inst.A!, ReadOperand(inst.B!));
                        break;
                    }
                    case Opcode.GetField:
                    {
                        Variable? fieldValue;
                        var obj = ReadOperand(inst.B!);
                        var fieldIdx = inst.C!.Value;
                        if (fieldIdx == 0)
                        {
                            // TODO Strings are actually Structs, when implemented in the language this hack can be fixed
                            if (obj.Value is byte[])
                                fieldValue = new Variable("$imm", CobType.U64, false, obj.BufferValue.Length);
                            else
                                fieldValue = obj.RecordValue[0];
                        }
                        else
                            fieldValue = obj.RecordValue[fieldIdx];

                        WriteOperand(inst.A!, fieldValue);
                        break;
                    }
                    case Opcode.SetField:
                    {
                        var obj = ReadOperand(inst.A!);
                        var fieldIdx = inst.B!.Value;
                        var value = ReadOperand(inst.C!);
                        obj.RecordValue[fieldIdx] = value;
                        break;
                    }
                    case Opcode.GetElem:
                    {
                        Variable? elemValue;
                        var arr = ReadOperand(inst.B!);
                        var idx = ReadOperand(inst.C!);
                        elemValue = arr.ElementAt(idx.IntValue);
                        WriteOperand(inst.A!, elemValue);
                        break;
                    }
                    case Opcode.SetElem:
                    {
                        throw new NotImplementedException();
                        break;
                    }
                    case Opcode.Peek:
                    {
                        var ptr = (IntPtr)ReadOperand(inst.B!).IntValue;
                        var size = ReadOperand(inst.C!).IntValue;
                        var value = size switch
                        {
                            1 => Marshal.ReadByte(ptr),
                            2 => Marshal.ReadInt16(ptr),
                            4 => Marshal.ReadInt32(ptr),
                            8 => Marshal.ReadInt64(ptr),
                            _ => throw new InvalidOperationException($"Cannot peek size {size}")
                        };
                        WriteOperand(inst.A!, value.ToCobVariable());
                        break;
                    }
                    case Opcode.Poke:
                    {
                        var ptr = (IntPtr)ReadOperand(inst.A!).IntValue;
                        var size = ReadOperand(inst.B!).IntValue;
                        var value = ReadOperand(inst.C!).IntValue;
                        switch (size)
                        {
                            case 1: Marshal.WriteByte(ptr, (byte)value); break;
                            case 2: Marshal.WriteInt16(ptr, (short)value); break;
                            case 4: Marshal.WriteInt32(ptr, (int)value); break;
                            case 8: Marshal.WriteInt64(ptr, value); break;
                            default: throw new InvalidOperationException($"Cannot poke size {size}");
                        }
                        break;
                    }
                    case Opcode.New:
                    {
                        var type = ReadOperand(inst.B!).Type;
                        Variable obj;
                        if (type.Tag is RecordType recordType)
                            obj = recordType.ToVariable();
                        else
                            throw new InvalidOperationException($"Cannot allocate type {type.Tag?.GetType().Name}");

                        WriteOperand(inst.A!, obj);
                        break;
                    }
                    case Opcode.BitShr:
                    {
                        var b = ReadOperand(inst.B!).IntValue;
                        b >>= (int)ReadOperand(inst.C!).IntValue;
                        WriteOperand(inst.A!, b.ToCobVariable());
                        break;
                    }
                    case Opcode.BitShl:
                    {
                        var b = ReadOperand(inst.B!).IntValue;
                        b <<= (int)ReadOperand(inst.C!).IntValue;
                        WriteOperand(inst.A!, b.ToCobVariable());
                        break;
                    }
                    case Opcode.BitRol:
                    {
                        var b = ReadOperand(inst.B!).IntValue;
                        var c = ReadOperand(inst.C!).IntValue;
                        b = (long)BitOperations.RotateLeft((ulong)b, (int)c);
                        WriteOperand(inst.A!, b.ToCobVariable());
                        break;
                    }
                    case Opcode.BitRor:
                    {
                        var b = ReadOperand(inst.B!).IntValue;
                        var c = ReadOperand(inst.C!).IntValue;
                        b = (long)BitOperations.RotateRight((ulong)b, (int)c);
                        WriteOperand(inst.A!, b.ToCobVariable());
                        break;
                    }
                    case Opcode.BitAnd:
                    {
                        var b = ReadOperand(inst.B!).IntValue;
                        b &= ReadOperand(inst.C!).IntValue;
                        WriteOperand(inst.A!, b.ToCobVariable());
                        break;
                    }
                    case Opcode.BitXor:
                    {
                        var b = ReadOperand(inst.B!).IntValue;
                        b ^= ReadOperand(inst.C!).IntValue;
                        WriteOperand(inst.A!, b.ToCobVariable());
                        break;
                    }
                    case Opcode.BitOr:
                    {
                        var b = ReadOperand(inst.B!).IntValue;
                        b |= ReadOperand(inst.C!).IntValue;
                        WriteOperand(inst.A!, b.ToCobVariable());
                        break;
                    }
                    case Opcode.BitNot:
                    {
                        var b = ReadOperand(inst.B!).IntValue;
                        b = ~b;
                        WriteOperand(inst.A!, b.ToCobVariable());
                        break;
                    }
                    case Opcode.Not:
                    {
                        var b = ReadOperand(inst.B!).IntValue;
                        b = b != 0 ? 0 : 1;
                        WriteOperand(inst.A!, b.ToCobVariable());
                        break;
                    }
                    case Opcode.Neg:
                    {
                        var b = ReadOperand(inst.B!).IntValue;
                        b = -b;
                        WriteOperand(inst.A!, b.ToCobVariable());
                        break;
                    }
                    case Opcode.Add:
                    {
                        var b = ReadOperand(inst.B!).IntValue;
                        b += ReadOperand(inst.C!).IntValue;
                        WriteOperand(inst.A!, b.ToCobVariable());
                        break;
                    }
                    case Opcode.Sub:
                    {
                        var b = ReadOperand(inst.B!).IntValue;
                        b -= ReadOperand(inst.C!).IntValue;
                        WriteOperand(inst.A!, b.ToCobVariable());
                        break;
                    }
                    case Opcode.Mul:
                    {
                        var b = ReadOperand(inst.B!).IntValue;
                        b *= ReadOperand(inst.C!).IntValue;
                        WriteOperand(inst.A!, b.ToCobVariable());
                        break;
                    }
                    case Opcode.Pow:
                    {
                        var b = ReadOperand(inst.B!).IntValue;
                        var c = ReadOperand(inst.C!).IntValue;
                        b = (long)Math.Pow(b, c);
                        WriteOperand(inst.A!, b.ToCobVariable());
                        break;
                    }
                    case Opcode.Div:
                    {
                        var b = ReadOperand(inst.B!).IntValue;
                        b /= ReadOperand(inst.C!).IntValue;
                        WriteOperand(inst.A!, b.ToCobVariable());
                        break;
                    }
                    case Opcode.DivCeil:
                    {
                        var b = ReadOperand(inst.B!).IntValue;
                        var c = ReadOperand(inst.C!).IntValue;
                        b = (b + c - 1) / c;
                        WriteOperand(inst.A!, b.ToCobVariable());
                        break;
                    }
                    case Opcode.DivFloor:
                    {
                        // TODO Technically only useful for floats
                        var b = ReadOperand(inst.B!).IntValue;
                        b /= ReadOperand(inst.C!).IntValue;
                        WriteOperand(inst.A!, b.ToCobVariable());
                        break;
                    }
                    case Opcode.Mod:
                    {
                        var b = ReadOperand(inst.B!).IntValue;
                        b %= ReadOperand(inst.C!).IntValue;
                        WriteOperand(inst.A!, b.ToCobVariable());
                        break;
                    }
                    case Opcode.Rem:
                    {
                        var b = ReadOperand(inst.B!).IntValue;
                        var c = ReadOperand(inst.C!).IntValue;
                        b = ((b % c) + c) % c;
                        WriteOperand(inst.A!, b.ToCobVariable());
                        break;
                    }
                    case Opcode.CondAnd:
                    {
                        var b = ReadOperand(inst.B!).IntValue;
                        var c = ReadOperand(inst.C!).IntValue;
                        var d = b == 1 && c == 1;
                        WriteOperand(inst.A!, d.ToCobVariable());
                        break;
                    }
                    case Opcode.CondOr:
                    {
                        var b = ReadOperand(inst.B!).IntValue;
                        var c = ReadOperand(inst.C!).IntValue;
                        var d = b == 1 || c == 1;
                        WriteOperand(inst.A!, d.ToCobVariable());
                        break;
                    }
                    case Opcode.CmpEQ:
                    {
                        var b = ReadOperand(inst.B!).IntValue;
                        var c = ReadOperand(inst.C!).IntValue;
                        var d = b == c;
                        WriteOperand(inst.A!, d.ToCobVariable());
                        break;
                    }
                    case Opcode.CmpNEQ:
                    {
                        var b = ReadOperand(inst.B!).IntValue;
                        var c = ReadOperand(inst.C!).IntValue;
                        var d = b != c;
                        WriteOperand(inst.A!, d.ToCobVariable());
                        break;
                    }
                    case Opcode.CmpLT:
                    {
                        var b = ReadOperand(inst.B!).IntValue;
                        var c = ReadOperand(inst.C!).IntValue;
                        var d = b < c;
                        WriteOperand(inst.A!, d.ToCobVariable());
                        break;
                    }
                    case Opcode.CmpLTE:
                    {
                        var b = ReadOperand(inst.B!).IntValue;
                        var c = ReadOperand(inst.C!).IntValue;
                        var d = b <= c;
                        WriteOperand(inst.A!, d.ToCobVariable());
                        break;
                    }
                    case Opcode.CmpGT:
                    {
                        var b = ReadOperand(inst.B!).IntValue;
                        var c = ReadOperand(inst.C!).IntValue;
                        var d = b > c;
                        WriteOperand(inst.A!, d.ToCobVariable());
                        break;
                    }
                    case Opcode.CmpGTE:
                    {
                        var b = ReadOperand(inst.B!).IntValue;
                        var c = ReadOperand(inst.C!).IntValue;
                        var d = b >= c;
                        WriteOperand(inst.A!, d.ToCobVariable());
                        break;
                    }
                    case Opcode.CmpTyEQ:
                    {
                        var b = ReadOperand(inst.B!).Type;
                        var c = ReadOperand(inst.C!).Type;
                        var d = b == c;
                        WriteOperand(inst.A!, d.ToCobVariable());
                        break;
                    }
                    case Opcode.CmpTyNEQ:
                    {
                        var b = ReadOperand(inst.B!).Type;
                        var c = ReadOperand(inst.C!).Type;
                        var d = b != c;
                        WriteOperand(inst.A!, d.ToCobVariable());
                        break;
                    }
                    case Opcode.JmpT:
                    {
                        var cmp = ReadOperand(inst.A!).IntValue;
                        if (cmp == 1)
                            ip = function.Body.Labels[(int)inst.B!.Value].Location - 1;
                        break;
                    }
                    case Opcode.JmpF:
                    {
                        var cmp = ReadOperand(inst.A!).IntValue;
                        if (cmp == 0)
                            ip = function.Body.Labels[(int)inst.B!.Value].Location - 1;
                        break;
                    }
                    case Opcode.Jmp:
                    {
                        ip = function.Body.Labels[(int)inst.A!.Value].Location - 1;
                        break;
                    }
                    case Opcode.PanicOnErr:
                    {
                        var a = ReadOperand(inst.A!);
                        if (a.Type == eCobType.Error)
                            throw new InvalidOperationException($"Unhandled error returned from function call in '{function.Name}' @ {ip}");
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            throw new InvalidOperationException("End of buffer without a ret instruction");

            void WriteOperand(Operand operand, Variable value)
            {
                switch (operand.Type)
                {
                    case OperandType.ImmediateSigned:
                    case OperandType.ImmediateUnsigned:
                    case OperandType.ImmediateFloat:
                    case OperandType.Function:
                        throw new InvalidOperationException();
                    case OperandType.Register:
                        registers[operand.Value] = value;
                        break;
                    case OperandType.Local:
                        locals[(int)operand.Value] = value;
                        break;
                    case OperandType.Global:
                        artifact.Globals[(int)operand.Value] = value;
                        break;
                    case OperandType.Argument:
                        parameters[(int)operand.Value] = value;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            
            Variable ReadOperand(Operand operand)
            {
                switch (operand.Type)
                {
                    case OperandType.ImmediateSigned:
                        return new Variable("$imm", CobType.Int, false, operand.Value);
                    case OperandType.ImmediateUnsigned:
                        return new Variable("$imm", CobType.UInt, false, operand.Value);
                    case OperandType.ImmediateFloat:
                        return new Variable("$imm", CobType.Float, false, operand.Value);
                    case OperandType.Register:
                        return registers[operand.Value];
                    case OperandType.Argument:
                        return parameters[(int)operand.Value];
                    case OperandType.Local:
                        return locals[(int)operand.Value];
                    case OperandType.Global:
                        return artifact.Globals[(int)operand.Value];
                    case OperandType.Function:
                    {
                        var function = artifact.Functions[(int)operand.Value];
                        var type = new CobType(eCobType.Function, tag: function);

                        return new Variable("$func", type, false) { Value = function };
                    }
                    case OperandType.Type:
                    {
                        var type = CobType.FromOperand(operand, artifact);
                        return new Variable("$type", type, false);
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        public void Dispose()
        {
            nativeLibrariesProxy.Dispose();
        }

        public sealed class NativeLibrariesProxy : IDisposable
        {
            public delegate long NativeWrapperDelegate(Variable[]? variables);

            private readonly Dictionary<string, NativeLibraryProxy> proxies;

            private NativeLibrariesProxy(Dictionary<string, NativeLibraryProxy> proxies)
            {
                this.proxies = proxies;
            }

            public Variable? Invoke(Import native, IList<Variable>? parameters)
            {
                if (native.Function == null)
                    return null;

                long result;
                var proxy = proxies[native.Library];
                var methodDelegate = proxy.Functions[native.SymbolName!];
                if (native.SymbolName == "printf") // HACK!!! REMOVE WHEN FORMATTING IS NATIVE
                {
                    Console.Write(
                        Encoding.UTF8.GetString(parameters[0].BufferValue),
                        parameters.Skip(1).Select(x =>
                        {
                            if (x.Type == CobType.String)
                                return Encoding.UTF8.GetString(x.BufferValue);

                            return x.Value;
                        }).ToArray()
                    );
                    result = 0;
                }
                else
                    result = methodDelegate(parameters?.ToArray());

                return new Variable("$imm", CobType.U64, false, result); // TODO Proxy more types than integers
            }

            public static NativeLibrariesProxy FromArtifact(Artifact artifact)
            {
                var proxies = new Dictionary<string, NativeLibraryProxy>();

                foreach (var import in artifact.Imports)
                {
                    if (!proxies.TryGetValue(import.Library, out var proxy))
                    {
                        var nativeLibrary = System.Runtime.InteropServices.NativeLibrary.Load(import.Library);
                        proxy = new NativeLibraryProxy(nativeLibrary);
                        proxies.Add(import.Library, proxy);
                    }

                    var address = System.Runtime.InteropServices.NativeLibrary.GetExport(proxy.Library, import.SymbolName!);

                    var method = new System.Reflection.Emit.DynamicMethod(
                        $"dynm_{import.SymbolName}",
                        typeof(long),
                        new [] { typeof(Variable[]) },
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
                            il.Emit(System.Reflection.Emit.OpCodes.Callvirt, typeof(Variable).GetProperty(nameof(Variable.BufferValue))!.GetGetMethod()!);
                            il.Emit(System.Reflection.Emit.OpCodes.Call, typeof(NativeLibrariesProxy).GetMethod(nameof(NativeLibrariesProxy.GetString))!);
                        }
                        else
                            il.Emit(System.Reflection.Emit.OpCodes.Callvirt, typeof(Variable).GetProperty(nameof(Variable.IntValue))!.GetGetMethod()!);
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
                    System.Runtime.InteropServices.NativeLibrary.Free(proxy.Library);

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
        private static readonly Variable True = new("$true", CobType.Boolean, false, 1);
        private static readonly Variable False = new("$false", CobType.Boolean, false, 0);

        public static Variable ToCobVariable(this long value)
        {
            return new Variable("$imm", CobType.U64, false, value);
        }

        public static Variable ToCobVariable(this bool value)
        {
            return value ? True : False;
        }
    }
}
