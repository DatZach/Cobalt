using System.Data;
using System.Diagnostics;
using System.Text;
using Compiler.Ast.Expressions.Statements;

namespace Compiler.CodeGeneration.Platform
{
    internal sealed class X86Assembler : ArtifactAssembler
    {
        public const string DefaultFasmPath = @"C:\Tools\fasmw17330\fasm.exe";

        public override IReadOnlyList<string> SupportedPlatforms => new[] { "x86", "x86_64" };

        public override string DefaultExtension => "exe";

        private Function currentFunction;
        private readonly CobType[] registerTypes = new CobType[32];
        private readonly CobType[] argumentTypes = new CobType[32];
        private CobType[] localTypes = null;

        private Compiler compiler;
        private bool is64Bit;

        public override void Assemble(Compiler compiler, ArtifactExpression artifact, string outputFilename)
        {
            this.compiler = compiler;
            is64Bit = artifact.Platform == "x86_64";

            var buffer = new MachineCodeBuffer();
            EmitProgram(buffer, artifact, outputFilename);
            Assemble(buffer, outputFilename);
        }

        private void EmitProgram(MachineCodeBuffer buffer, ArtifactExpression artifact, string outputFilename)
        {
            if (compiler.Exports.Count == 0)
                throw new Exception("Program is exportless");

            var hasEntryPoint = compiler.Exports.TryGetValue("Main", out var entryPointName);

            // Preamble
            buffer.Emit("format ");
            buffer.Emit(artifact.Container);
            buffer.Emit(" ");
            if (artifact.ContainerParameters.Count > 0)
                buffer.Emit(string.Join(' ', artifact.ContainerParameters));
            else
                buffer.Emit("console");
            buffer.EmitLine("");
            if (hasEntryPoint) buffer.EmitLine("entry start");
            buffer.EmitLine(is64Bit ? "use64" : "use32");
            buffer.EmitLine("include 'include/win32a.inc'");

            // Text
            buffer.EmitLine("section '.text' code executable");

            // Entry Point
            if (hasEntryPoint)
            {
                buffer.EmitLine("start:");
                buffer.EmitLine("    push __UnhandledExceptionHandler");
                buffer.EmitLine("    call [SetUnhandledExceptionFilter]");
                buffer.EmitLine("    call " + entryPointName);
                buffer.EmitLine("    push 0");
                buffer.EmitLine("    call [ExitProcess]");

                buffer.EmitLine("__UnhandledExceptionHandler:");
                buffer.EmitLine("    push _msg_UnhandledException");
                buffer.EmitLine("    call [printf]");
                buffer.EmitLine("    mov eax, 1");
                buffer.EmitLine("    ret");

                compiler.Imports.Add(new Import
                {
                    Library = "kernel32",
                    SymbolName = "SetUnhandledExceptionFilter"
                });
                compiler.Imports.Add(new Import
                {
                    Library = "kernel32",
                    SymbolName = "ExitProcess"
                });
            }

            for (var i = 0; i < compiler.Functions.Count; i++)
            {
                var f = compiler.Functions[i];
                if (f.NativeImport != null)
                    continue;

                var stackSpace = f.ResolveStackSpaceRequired();
                buffer.EmitLine(f.Name + ":");
                if (f.CallingConvention != CallingConvention.None)
                {
                    buffer.EmitLine("push ebp");
                    buffer.EmitLine("mov ebp, esp");
                    if (stackSpace > 0)
                        buffer.EmitLine($"sub esp, {stackSpace}");
                    for (var j = 3; j < 32; j++)
                    {
                        if ((f.ClobberedRegisters & (1u << j)) == 0)
                            continue;

                        // EAX, ECX, EDX are not preserved in cdecl
                        buffer.EmitLine($"push {GetIntegerRegisterName(j)}");
                    }
                }

                currentFunction = f;
                EmitIntermediateInstructionBuffer(buffer, f.Body);

                buffer.EmitLine(".return:");
                if (f.CallingConvention != CallingConvention.None)
                {
                    for (var j = 32 - 1; j >= 3; --j)
                    {
                        if ((f.ClobberedRegisters & (1u << j)) == 0)
                            continue;

                        buffer.EmitLine($"pop {GetIntegerRegisterName(j)}");
                    }

                    buffer.EmitLine("leave");

                    buffer.EmitLine(
                        f.CallingConvention == CallingConvention.CCall
                            ? "ret"
                            : $"ret {stackSpace}"
                    );
                }
            }

            // Readonly Data
            buffer.EmitLine("section '.rdata' data readable");
            for (var i = 0; i < compiler.Globals.Count; i++)
            {
                var global = compiler.Globals[i];
                switch (global.Type.Type)
                {
                    case eCobType.Signed:
                    case eCobType.Unsigned:
                    {
                        var describe = global.Type.Size switch
                        {
                            64 => "dq",
                            32 => "dd",
                            16 => "dw",
                            8 => "db",
                            _ => throw new DataException($"Cannot encode {global.Type.Size} bits of data")
                        };
                        buffer.EmitLine($"rdata_{i} {describe} {global.Value}");
                        break;
                    }
                    case eCobType.Float:
                    {
                        var describe = global.Type.Size switch
                        {
                            64 => "dq",
                            32 => "dd",
                            _ => throw new DataException($"Cannot encode {global.Type.Size} bits of data")
                        };
                        buffer.EmitLine($"rdata_{i} {describe} {BitConverter.Int64BitsToDouble(global.Value)}");
                        break;
                    }
                    case eCobType.Function:
                        buffer.EmitLine($"rdata_{i} dd {global.Type.Function.Name}");
                        break;
                    case eCobType.Array:
                    case eCobType.String:
                        if (global.Data == null)
                            throw new DataException("Data expected for string or array type");

                        var value = string.Join(", ", global.Data);
                        buffer.EmitLine($"rdata_{i} db {value}");
                        break;
                    default:
                        throw new DataException($"Cannot encode .rdata {global.Type}");
                }
            }

            buffer.EmitLine("_msg_UnhandledException db 'UNHANDLED EXCEPTION', 0");

            // Imports
            buffer.EmitLine("section '.idata' data readable import");

            var libraries = compiler.Imports.Where(x => x.SymbolName != null)
                                            .Select(x => x.Library)
                                            .Distinct()
                                            .ToList();
            for (var i = 0; i < libraries.Count; i++)
            {
                var library = libraries[i];
                string line = "";

                if (i == 0) line += "library ";
                else line += "        ";

                line += library;
                line += ", '";
                line += library;
                line += ".dll'";

                if (i != libraries.Count - 1)
                    line += ", \\";

                buffer.EmitLine(line);
            }

            foreach (var library in libraries)
            {
                buffer.Emit($"import {library}");
                for (var i = 0; i < compiler.Imports.Count; i++)
                {
                    var import = compiler.Imports[i];
                    if (import.Library != library || import.SymbolName == null)
                        continue;

                    buffer.EmitLine(", \\");
                    buffer.Emit($"    {import.SymbolName}, '{import.SymbolName}'");
                }

                buffer.EmitLine();
            }
            
            // Exports
            if ((!hasEntryPoint && compiler.Exports.Count > 0)
            ||  (hasEntryPoint && compiler.Exports.Count > 1))
            {
                buffer.EmitLine("section '.edata' export data readable");
                buffer.EmitLine($"export '{outputFilename}', \\");

                int i = 0;
                foreach (var export in compiler.Exports)
                {
                    buffer.Emit($"    {export.Value}, '{export.Key}'");
                    if (i++ != compiler.Exports.Count - 1)
                        buffer.EmitLine(", \\");
                }

                buffer.EmitLine();
            }
        }

        private void EmitIntermediateInstructionBuffer(MachineCodeBuffer buffer, InstructionBuffer body)
        {
            Array.Fill(registerTypes, CobType.None);
            Array.Fill(argumentTypes, CobType.None); // TODO Not right
            localTypes = new CobType[currentFunction.Locals.Count];
            Array.Fill(localTypes, CobType.None);

            for (var i = 0; i < body.Instructions.Count; i++)
            {
                var inst = body.Instructions[i];
                switch (inst.Opcode)
                {
                    case Opcode.None:
                        buffer.EmitLine("nop");
                        break;
                    case Opcode.Call:
                    {
                        var callee = GetOperandDataType(inst.A).Function;
                        int stackSpace = 0;
                        if (inst.C != null)
                        {
                            if (callee.CallingConvention != CallingConvention.CCall)
                            {
                                for (int j = 0; j < inst.C.Count; ++j)
                                    stackSpace += EmitArgument(buffer, inst.C[j]);
                            }
                            else
                            {
                                for (int j = inst.C.Count - 1; j >= 0; --j)
                                    stackSpace += EmitArgument(buffer, inst.C[j]);
                            }
                        }

                        buffer.Emit("call ");
                        buffer.EmitLine(GetOperandString(inst.A));

                        if (callee.CallingConvention == CallingConvention.CCall && stackSpace > 0)
                            buffer.EmitLine($"add esp, {stackSpace}");
                        break;
                    }
                    case Opcode.Return:
                    {
                        if (inst.A != null)
                            buffer.EmitLine($"mov eax, {GetOperandString(inst.A)}");
                        buffer.EmitLine("jmp .return");
                        break;
                    }
                    case Opcode.RestoreStack:
                        buffer.EmitLine($"add esp, {GetOperandString(inst.A)}");
                        break;
                    case Opcode.Move:
                    {
                        var aType = GetOperandDataType(inst.A);
                        var bType = GetOperandDataType(inst.B);

                        if (bType == eCobType.Float)
                            buffer.Emit(bType.Size == 32 ? "movss " : "movsd "); // TODO Support diff sizes
                        else
                            buffer.Emit("mov ");

                        buffer.Emit(GetOperandString(inst.A, bType));
                        buffer.Emit(", ");
                        buffer.EmitLine(GetOperandString(inst.B, bType));

                        SetMachineState(inst.A, inst.B);
                        break;
                    }
                    //case Opcode.Push:
                    //{
                    //    if (inst.A.Type == OperandType.Register && registerTypes[(int)inst.A.Value] == eCobType.Float)
                    //    {
                    //        var operand = GetFloatRegisterName((int)inst.A.Value);
                    //        buffer.EmitLine($"cvtss2sd {operand}, {operand}");
                    //        buffer.EmitLine("sub esp, 8");
                    //        buffer.EmitLine($"mov dword [esp], {operand}");
                    //    }
                    //    else
                    //    {
                    //        buffer.Emit("push ");
                    //        buffer.EmitLine(GetOperandString(inst.A));
                    //    }
                    //    break;
                    //}
                    //case Opcode.Pop:
                    //    buffer.Emit("pop ");
                    //    buffer.EmitLine(GetOperandString(inst.A));
                    //    break;
                    case Opcode.BitShr:
                        buffer.Emit("shr ");
                        buffer.Emit(GetOperandString(inst.A));
                        buffer.Emit(", ");
                        buffer.EmitLine(GetOperandString(inst.B));
                        break;
                    case Opcode.BitShl:
                        buffer.Emit("shl ");
                        buffer.Emit(GetOperandString(inst.A));
                        buffer.Emit(", ");
                        buffer.EmitLine(GetOperandString(inst.B));
                        break;
                    case Opcode.BitAnd:
                        buffer.Emit("and ");
                        buffer.Emit(GetOperandString(inst.A));
                        buffer.Emit(", ");
                        buffer.EmitLine(GetOperandString(inst.B));
                        break;
                    case Opcode.BitXor:
                        buffer.Emit("xor ");
                        buffer.Emit(GetOperandString(inst.A));
                        buffer.Emit(", ");
                        buffer.EmitLine(GetOperandString(inst.B));
                        break;
                    case Opcode.BitOr:
                        buffer.Emit("or ");
                        buffer.Emit(GetOperandString(inst.A));
                        buffer.Emit(", ");
                        buffer.EmitLine(GetOperandString(inst.B));
                        break;
                    case Opcode.Add:
                    {
                        var aType = GetOperandDataType(inst.A);
                        if (aType == eCobType.Float)
                            buffer.Emit(aType.Size == 32 ? "addss " : "addsd "); // TODO Account for width
                        else
                            buffer.Emit("add ");
                        buffer.Emit(GetOperandString(inst.A));
                        buffer.Emit(", ");
                        buffer.EmitLine(GetOperandString(inst.B));
                        break;
                    }
                    case Opcode.Sub:
                    {
                        var aType = GetOperandDataType(inst.A);
                        if (aType == eCobType.Float)
                            buffer.Emit(aType.Size == 32 ? "subss " : "subsd "); // TODO Account for width
                        else
                            buffer.Emit("sub ");
                        buffer.Emit(GetOperandString(inst.A));
                        buffer.Emit(", ");
                        buffer.EmitLine(GetOperandString(inst.B));
                        break;
                    }
                    case Opcode.Mul:
                    {
                        var aType = GetOperandDataType(inst.A);
                        if (aType == eCobType.Float)
                            buffer.Emit(aType.Size == 32 ? "mulss " : "mulsd "); // TODO Account for width
                        else
                            buffer.Emit("imul ");
                        buffer.Emit(GetOperandString(inst.A));
                        buffer.Emit(", ");
                        buffer.EmitLine(GetOperandString(inst.B));
                        break;
                    }
                    case Opcode.Div:
                    {
                        var aType = GetOperandDataType(inst.A);
                        if (aType == eCobType.Float)
                        {
                            buffer.Emit(aType.Size == 32 ? "divss " : "divsd ");
                            buffer.Emit(GetOperandString(inst.A));
                            buffer.Emit(", ");
                            buffer.EmitLine(GetOperandString(inst.B));
                        }
                        else
                        {
                            buffer.EmitLine("push eax");
                            buffer.EmitLine("push ebx");
                            buffer.EmitLine($"mov ebx, {GetOperandString(inst.B)}");
                            buffer.EmitLine($"mov eax, {GetOperandString(inst.A)}");
                            buffer.EmitLine("cdq");
                            buffer.EmitLine("idiv ebx");
                            buffer.EmitLine($"mov {GetOperandString(inst.A)}, eax");
                            buffer.EmitLine("pop ebx");
                            buffer.EmitLine("pop eax");
                        }
                        break;
                    }
                    case Opcode.Mod:
                    {
                        var aType = GetOperandDataType(inst.A);
                        if (aType == eCobType.Float)
                        {
                            throw new NotImplementedException();
                        }
                        else
                        {
                            buffer.EmitLine("push eax");
                            buffer.EmitLine("push ebx");
                            buffer.EmitLine($"mov ebx, {GetOperandString(inst.B)}");
                            buffer.EmitLine($"mov eax, {GetOperandString(inst.A)}");
                            buffer.EmitLine("cdq");
                            buffer.EmitLine("idiv ebx");
                            buffer.EmitLine($"mov {GetOperandString(inst.A)}, edx");
                            buffer.EmitLine("pop ebx");
                            buffer.EmitLine("pop eax");
                        }
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private int EmitArgument(MachineCodeBuffer buffer, Operand operand)
        {
            var cType = GetOperandDataType(operand);
            var cOperand = GetOperandString(operand);
            if (cType == eCobType.Float)
            {
                if (cType.Size == 32)
                    buffer.EmitLine($"cvtss2sd {cOperand}, {cOperand}");
                buffer.EmitLine("sub esp, 8");
                buffer.EmitLine($"movsd qword [esp], {cOperand}");
            }
            else
                buffer.EmitLine($"push {cOperand}");

            return cType.Size / 8;
        }

        private void SetMachineState(Operand operandA, Operand operandB)
        {
            var typeB = GetOperandDataType(operandB);
            switch (operandA.Type)
            {
                case OperandType.None:
                case OperandType.ImmediateSigned:
                case OperandType.ImmediateUnsigned:
                case OperandType.ImmediateFloat:
                    // TODO Technically an error condition?
                    break;
                case OperandType.Register:
                    // NOTE xmm and **x registers share type state in VM bytecode
                    registerTypes[(int)operandA.Value] = typeB;
                    break;
                case OperandType.Argument:
                    argumentTypes[(int)operandA.Value] = typeB;
                    break;
                case OperandType.Local:
                    localTypes[(int)operandA.Value] = typeB;
                    // throw new InvalidOperationException(); // ???
                    //currentFunction.Locals[(int)operandA.Value] = typeB;
                    break;
                case OperandType.Global:
                    // ???
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private CobType GetOperandDataType(Operand operand)
        {
            switch (operand.Type)
            {
                case OperandType.ImmediateSigned:
                    return new CobType(eCobType.Signed, operand.Size);
                case OperandType.ImmediateUnsigned:
                    return new CobType(eCobType.Unsigned, operand.Size);
                case OperandType.ImmediateFloat:
                    return new CobType(eCobType.Float, operand.Size);
                case OperandType.Register:
                    return registerTypes[operand.Value];
                case OperandType.Argument:
                    return argumentTypes[operand.Value];
                case OperandType.Local:
                    return localTypes[(int)operand.Value];
                case OperandType.Global:
                    return compiler.Globals[(int)operand.Value].Type;
                default:
                    return CobType.None;
            }
        }

        // TODO Support 64bit correctly
        private string GetOperandString(Operand? operand, CobType? typeOverride = null)
        {
            if (operand == null) throw new ArgumentNullException(nameof(operand));
            
            var describe = operand.Size switch
            {
                0 => is64Bit ? "qword" : "dword",
                8 => "byte",
                16 => "word",
                32 => "dword",
                64 => "qword",
                _ => throw new DataException($"Cannot describe immediate of {operand.Size} bits")
            };

            switch (operand.Type)
            {
                case OperandType.None:
                    return "";
                case OperandType.ImmediateSigned:
                    return operand.Value.ToString("D");
                case OperandType.ImmediateUnsigned:
                    return ((ulong)operand.Value).ToString("D");
                case OperandType.ImmediateFloat:
                {
                    var valueName = BitConverter.Int64BitsToDouble(operand.Value).ToString("F").Replace('.', '_');
                    valueName = $"real_{operand.Size}_{valueName}";
                    var globalIdx = compiler.FindGlobal(valueName);
                    if (globalIdx == -1)
                    {
                        globalIdx = compiler.AllocateGlobal(new CobVariable(
                            valueName,
                            new CobType(eCobType.Float, operand.Size),
                            operand.Value
                        ));
                    }
                    
                    return $"{describe} [rdata_{globalIdx}]";
                }
                case OperandType.Register:
                {
                    var type = typeOverride ?? registerTypes[operand.Value];
                    if (type == eCobType.Float)
                        return GetFloatRegisterName((int)operand.Value);
                    else
                        return GetIntegerRegisterName((int)operand.Value);
                }
                case OperandType.Argument:
                    return $"{describe} [ebp + {operand.Value * 4 + 4 * 2}]"; // TODO Get offset correctly
                case OperandType.Local: // TODO Use size
                    return $"{describe} [ebp - {(operand.Value + 1) * 4}]";
                case OperandType.Global:
                {
                    var global = compiler.Globals[(int)operand.Value];
                    if (global.Type == eCobType.Function)
                    {
                        if (global.Type.Function.NativeImport != null)
                            return "[" + global.Type.Function.Name + "]";
                        return global.Type.Function.Name;
                    }

                    return $"rdata_{operand.Value}";
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static readonly string[] registers32 = { "eax", "ecx", "edx", "ebx" };
        private static readonly string[] registers64 = { "rax", "rcx", "rdx", "rbx", "r8",  "r9", 
                                                         "r10", "r11", "r12", "r13", "r14", "r15" };
        private string GetIntegerRegisterName(int i)
        {
            return is64Bit ? registers64[i] : registers32[i];
        }

        private static readonly string[] floatRegisters = { "xmm0", "xmm1", "xmm2", "xmm3" };
        private string GetFloatRegisterName(int i)
        {
            return floatRegisters[i];
        }

        private static void Assemble(MachineCodeBuffer buffer, string outputFilename)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (outputFilename == null) throw new ArgumentNullException(nameof(outputFilename));

            var asm = buffer.ToString();

            if (Program.Config.AssemblyVerboseOutput)
                Console.WriteLine(asm);

            var tmpFilename = Path.Combine(Path.GetTempPath(), Path.GetTempFileName());
            File.WriteAllText(tmpFilename, asm);

            try
            {
                var fasmPath = Program.Config.FasmPath ?? DefaultFasmPath;

                var process = new Process();
                process.StartInfo.FileName = fasmPath;
                process.StartInfo.Arguments = $"\"{tmpFilename}\"";
                process.StartInfo.WorkingDirectory = Path.GetDirectoryName(fasmPath);
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.ErrorDataReceived += Assembler_StdOutRecieved;
                if (Program.Config.FasmVerboseOutput)
                    process.OutputDataReceived += Assembler_StdOutRecieved;
                process.EnableRaisingEvents = true;

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    tmpFilename = Path.ChangeExtension(tmpFilename, "exe");
                    File.Copy(tmpFilename, outputFilename, true);
                }
                else
                    Console.WriteLine($"Error: Assembler exited with code {process.ExitCode}");
            }
            finally
            {
                File.Delete(tmpFilename);
            }

            static void Assembler_StdOutRecieved(object sender, DataReceivedEventArgs e)
            {
                Console.WriteLine(e.Data);
            }
        }
    }

    internal sealed class MachineCodeBuffer
    {
        private readonly StringBuilder builder;

        public MachineCodeBuffer()
        {
            builder = new StringBuilder(4096);
        }

        public void Emit(string op)
        {
            builder.Append(op);
        }

        public void EmitLine(string op)
        {
            builder.AppendLine(op);
        }

        public void EmitLine()
        {
            builder.AppendLine();
        }

        public override string ToString()
        {
            return builder.ToString();
        }
    }
}
