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
                        buffer.EmitLine($"push {GetRegisterName(j)}");
                    }
                }

                EmitIntermediateInstructionBuffer(buffer, f.Body);

                buffer.EmitLine(".return:");
                if (f.CallingConvention != CallingConvention.None)
                {
                    for (var j = 32 - 1; j >= 3; --j)
                    {
                        if ((f.ClobberedRegisters & (1u << j)) == 0)
                            continue;

                        buffer.EmitLine($"pop {GetRegisterName(j)}");
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
                if (global.Data == null)
                    continue;

                var value = string.Join(", ", global.Data);
                buffer.EmitLine($"rdata_{i} db {value}");
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
            for (var i = 0; i < body.Instructions.Count; i++)
            {
                var inst = body.Instructions[i];
                switch (inst.Opcode)
                {
                    case Opcode.None:
                        buffer.EmitLine("nop");
                        break;
                    case Opcode.Call:
                        buffer.Emit("call ");
                        buffer.EmitLine(GetOperandString(inst.A));
                        break;
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
                        buffer.Emit("mov ");
                        buffer.Emit(GetOperandString(inst.A));
                        buffer.Emit(", ");
                        buffer.EmitLine(GetOperandString(inst.B));
                        break;
                    case Opcode.Push:
                        buffer.Emit("push ");
                        buffer.EmitLine(GetOperandString(inst.A));
                        break;
                    case Opcode.Pop:
                        buffer.Emit("pop ");
                        buffer.EmitLine(GetOperandString(inst.A));
                        break;
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
                        buffer.Emit("add ");
                        buffer.Emit(GetOperandString(inst.A));
                        buffer.Emit(", ");
                        buffer.EmitLine(GetOperandString(inst.B));
                        break;
                    case Opcode.Sub:
                        buffer.Emit("sub ");
                        buffer.Emit(GetOperandString(inst.A));
                        buffer.Emit(", ");
                        buffer.EmitLine(GetOperandString(inst.B));
                        break;
                    case Opcode.Mul:
                        buffer.Emit("imul "); // TODO unsigned version too
                        buffer.Emit(GetOperandString(inst.A));
                        buffer.Emit(", ");
                        buffer.EmitLine(GetOperandString(inst.B));
                        break;
                    case Opcode.Div:
                        buffer.EmitLine("push eax");
                        buffer.EmitLine("push ebx");
                        buffer.EmitLine($"mov ebx, {GetOperandString(inst.B)}");
                        buffer.EmitLine($"mov eax, {GetOperandString(inst.A)}");
                        buffer.EmitLine("cdq");
                        buffer.EmitLine("idiv ebx");
                        buffer.EmitLine($"mov {GetOperandString(inst.A)}, eax");
                        buffer.EmitLine("pop ebx");
                        buffer.EmitLine("pop eax");
                        break;
                    case Opcode.Mod:
                        buffer.EmitLine("push eax");
                        buffer.EmitLine("push ebx");
                        buffer.EmitLine($"mov ebx, {GetOperandString(inst.B)}");
                        buffer.EmitLine($"mov eax, {GetOperandString(inst.A)}");
                        buffer.EmitLine("cdq");
                        buffer.EmitLine("idiv ebx");
                        buffer.EmitLine($"mov {GetOperandString(inst.A)}, edx");
                        buffer.EmitLine("pop ebx");
                        buffer.EmitLine("pop eax");
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        // TODO Support 64bit correctly
        private string GetOperandString(Operand? operand)
        {
            if (operand == null) throw new ArgumentNullException(nameof(operand));

            switch (operand.Type)
            {
                case OperandType.None:
                    return "";
                case OperandType.ImmediateSigned:
                    return operand.Value.ToString("D");
                case OperandType.ImmediateUnsigned:
                    return ((ulong)operand.Value).ToString("D");
                case OperandType.ImmediateFloat:
                    return BitConverter.Int64BitsToDouble(operand.Value).ToString("F");
                case OperandType.Register:
                    return GetRegisterName((int)operand.Value);
                case OperandType.Pointer:
                    throw new NotImplementedException();
                case OperandType.Parameter:
                    return $"dword [ebp + {operand.Value * 4 + 4 * 2}]";
                case OperandType.Local: // TODO Use size
                    return $"dword [ebp - {(operand.Value + 1) * 4}]";
                case OperandType.Global:
                {
                    var fun = compiler.Globals[(int)operand.Value];
                    if (fun.Type == eCobType.Function)
                    {
                        if (fun.Type.Function.NativeImport != null)
                            return "[" + fun.Type.Function.Name + "]";
                        return fun.Type.Function.Name;
                    }
                    return $"rdata_{operand.Value}";
                }
                case OperandType.Function:
                {
                    var functionName = compiler.Functions[(int)operand.Value].Name;
                    if (compiler.Imports.Any(x => x.SymbolName == functionName)) // TODO Probably could improve
                        return "[" + functionName + "]";

                    return functionName;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static readonly string[] registers32 = { "eax", "ecx", "edx", "ebx" };
        private static readonly string[] registers64 = { "rax", "rcx", "rdx", "rbx", "r8",  "r9", 
                                                         "r10", "r11", "r12", "r13", "r14", "r15" };
        private string GetRegisterName(int i)
        {
            return is64Bit ? registers64[i] : registers32[i];
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

                tmpFilename = Path.ChangeExtension(tmpFilename, "exe");
                File.Copy(tmpFilename, outputFilename, true);
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
