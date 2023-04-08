using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

namespace Compiler.CodeGeneration.Platform
{
    internal static class X86Assembler
    {
        public const string DefaultFasmPath = @"C:\Tools\fasmw17330\fasm.exe";

        private static Compiler HACK_Compiler;

        public static void Assemble(Compiler compiler, string outputFilename)
        {
            HACK_Compiler = compiler; // HACK I made this a static class but I don't want to pass this further
            var buffer = new MachineCodeBuffer();
            EmitProgram(buffer, compiler);
            Assemble(buffer, outputFilename);
        }

        private static void EmitProgram(MachineCodeBuffer buffer, Compiler compiler)
        {
            compiler.Imports.Add(new Import
            {
                Library = "kernel32",
                SymbolName = "ExitProcess"
            });

            var hasEntryPoint = compiler.Exports.Contains("Main");

            // Preamble
            buffer.EmitLine("format PE console"); // TODO GUI subsystem
            if (hasEntryPoint) buffer.EmitLine("entry start");
            buffer.EmitLine("use32"); // TODO 64bit
            buffer.EmitLine("include 'include/win32a.inc'");

            buffer.EmitLine("section '.text' code executable");
            buffer.EmitLine("start:");
            buffer.EmitLine("        call Main");
            buffer.EmitLine("        push 0");
            buffer.EmitLine("        call [ExitProcess]");

            // Text
            foreach (var f in compiler.Functions)
            {
                // TODO HACK GARBAGE BAD CODE SLOW
                if (compiler.Imports.Any(x => x.SymbolName == f.Name))
                    continue;

                buffer.EmitLine(f.Name + ":");
                // TODO Support stdcall and naked
                buffer.EmitLine("push ebp");
                buffer.EmitLine("mov ebp, esp");
                buffer.EmitLine($"sub esp, {f.Locals.Count * 4}"); // TODO Calculate actual stack space required
                for (var i = 3; i < 32; i++) // TODO 64
                {
                    if ((f.ClobberedRegisters & (1u << i)) == 0)
                        continue;

                    // EAX, ECX, EDX are not preserved in cdecl TODO support others
                    buffer.EmitLine($"push {GetRegisterName(i)}");
                }

                EmitIntermediateInstructionBuffer(buffer, f.Body);

                buffer.EmitLine(".return:");
                for (var i = 32 - 1; i >= 3; --i) // TODO 64 + stdcall, naked, etc
                {
                    if ((f.ClobberedRegisters & (1u << i)) == 0)
                        continue;
                    
                    buffer.EmitLine($"pop {GetRegisterName(i)}");
                }

                buffer.EmitLine("leave");
                buffer.EmitLine("ret");
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

            foreach (var import in compiler.Imports)
            {
                if (import.SymbolName == null)
                    continue;

                buffer.EmitLine($"import {import.Library}, {import.SymbolName}, '{import.SymbolName}'");
            }
        }

        private static void EmitIntermediateInstructionBuffer(MachineCodeBuffer buffer, InstructionBuffer body)
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
                        buffer.EmitLine("push ebx");
                        buffer.EmitLine($"mov ebx, {GetOperandString(inst.B)}");
                        buffer.EmitLine($"mov ecx, {GetOperandString(inst.A)}");
                        buffer.EmitLine("cdq");
                        buffer.EmitLine("idiv ebx");
                        buffer.EmitLine($"mov {GetOperandString(inst.A)}, eax");
                        buffer.EmitLine("pop ebx");
                        break;
                    case Opcode.Mod:
                        buffer.EmitLine("push ebx");
                        buffer.EmitLine($"mov ebx, {GetOperandString(inst.B)}");
                        buffer.EmitLine($"mov ecx, {GetOperandString(inst.A)}");
                        buffer.EmitLine("cdq");
                        buffer.EmitLine("idiv ebx");
                        buffer.EmitLine($"mov {GetOperandString(inst.A)}, edx");
                        buffer.EmitLine("pop ebx");
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private static string GetOperandString(Operand? operand)
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
                case OperandType.Local: // TODO Use size
                    return $"dword [ebp - {(operand.Value + 1) * 4}]";
                case OperandType.Global:
                    return $"rdata_{operand.Value}";
                case OperandType.Function:
                {
                    var functionName = HACK_Compiler.Functions[(int)operand.Value].Name;
                    if (HACK_Compiler.Imports.Any(x => x.SymbolName == functionName)) // TODO Probably could improve
                        return "[" + functionName + "]";

                    return functionName;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static readonly string[] registers = { "eax", "ecx", "edx", "ebx" };
        private static string GetRegisterName(int i)
        {
            return registers[i];
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

        public override string ToString()
        {
            return builder.ToString();
        }
    }
}
