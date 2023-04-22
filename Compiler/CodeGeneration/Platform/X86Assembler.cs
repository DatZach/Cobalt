using System.Data;
using System.Diagnostics;
using System.Text;
using Compiler.Ast.Expressions.Statements;

namespace Compiler.CodeGeneration.Platform
{
    internal sealed class X86Assembler : ArtifactAssembler
    {
        public const string DefaultFasmPath = @"C:\Tools\fasmw17330\fasm.exe";

        public override IReadOnlyList<string> SupportedPlatforms => new[] { "x86_64" };

        public override string DefaultExtension => "exe";

        private Function currentFunction;
        private int callReserve, localReserve, nvrReserve, stackSpace;
        private readonly CobType[] registerTypes = new CobType[32];
        private readonly CobType[] argumentTypes = new CobType[32];
        private CobType[] localTypes = null;

        private Compiler compiler;

        public override void Assemble(Compiler compiler, ArtifactExpression artifact, string outputFilename)
        {
            this.compiler = compiler;

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
            buffer.Emit("64 ");
            if (artifact.ContainerParameters.Count > 0)
                buffer.Emit(string.Join(' ', artifact.ContainerParameters));
            else
                buffer.Emit("console");
            buffer.EmitLine("");
            if (hasEntryPoint) buffer.EmitLine("entry EntryPoint");
            buffer.EmitLine("include 'include/win64a.inc'");

            // Text
            buffer.EmitLine("section '.text' code executable");

            // Entry Point
            if (hasEntryPoint)
            {
                buffer.EmitLine("EntryPoint:");
                //buffer.EmitLine("    and     rsp, not 8");
                buffer.EmitLine("    sub     rsp, 32");
                buffer.EmitLine("    mov     rcx, __UnhandledExceptionHandler");
                buffer.EmitLine("    call    [SetUnhandledExceptionFilter]");
                buffer.EmitLine("    call    " + entryPointName);
                buffer.EmitLine("    xor     rcx, rcx");
                buffer.EmitLine("    call    [ExitProcess]");
                buffer.EmitLine("    add     rsp, 32");
                buffer.EmitLine("    xor     rax, rax");
                buffer.EmitLine("    ret");

                buffer.EmitLine("__UnhandledExceptionHandler:");
                buffer.EmitLine("    and     rsp, not 8");
                buffer.EmitLine("    sub     rsp, 32");
                buffer.EmitLine("    mov     ecx, __UnhandledExceptionHandler_Message");
                buffer.EmitLine("    call    [printf]");
                buffer.EmitLine("    mov     rax, 1");
                buffer.EmitLine("    add     rsp, 32");
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

                // TODO Clean this up
                callReserve = 0;
                var instructions = f.Body.Instructions;
                for (int j = 0; j < instructions.Count; ++j)
                {
                    var inst = instructions[j];
                    if (inst.Opcode == Opcode.Call)
                        callReserve = Math.Max(callReserve, 4);
                    else if (inst.Opcode == Opcode.Move && inst.A.Type == OperandType.Argument)
                        callReserve = Math.Max(callReserve, (int)inst.A.Value);
                }

                callReserve *= 8;

                localReserve = f.Locals.Count * 8;

                nvrReserve = 0;
                for (int j = 0; j < MaxRegisters; ++j)
                {
                    if ((f.ClobberedRegisters & (1u << j)) == 0)
                        continue;

                    var regName = GetIntegerRegisterName(j, 64);
                    if (nonVolatileRegisters.Contains(regName))
                        nvrReserve += 8;
                }

                stackSpace = callReserve + localReserve + nvrReserve;
                while (stackSpace % 16 != 0) ++stackSpace; // TODO Write a better implementation lol
                //stackSpace += 16 - (stackSpace & ~16);

                buffer.EmitLine(f.Name + ":");
                if (f.CallingConvention != CallingConvention.None)
                {
                    if (stackSpace > 0)
                        buffer.EmitLine($"sub rsp, {stackSpace}");
                    for (int j = 0, k = 0; j < MaxRegisters; j++) // TODO Clean this up
                    {
                        if ((f.ClobberedRegisters & (1u << j)) == 0)
                            continue;

                        var regName = GetIntegerRegisterName(j, 64);
                        if (nonVolatileRegisters.Contains(regName))
                        {
                            int nvrOffset = callReserve + localReserve + k * 8;
                            buffer.EmitLine($"mov qword [rsp + {nvrOffset}], {GetIntegerRegisterName(j, 64)}");
                        }
                    }
                }

                currentFunction = f;
                EmitIntermediateInstructionBuffer(buffer, f.Body);

                buffer.EmitLine(".return:");
                if (f.CallingConvention != CallingConvention.None)
                {
                    for (int j = 0, k = 0; j < MaxRegisters; j++) // TODO Clean this up
                    {
                        if ((f.ClobberedRegisters & (1u << j)) == 0)
                            continue;

                        var regName = GetIntegerRegisterName(j, 64);
                        if (nonVolatileRegisters.Contains(regName))
                        {
                            int nvrOffset = callReserve + localReserve + k * 8;
                            buffer.EmitLine($"mov {GetIntegerRegisterName(j, 64)}, qword [rsp + {nvrOffset}]");
                        }
                    }
                    
                    if (stackSpace > 0)
                        buffer.EmitLine($"add rsp, {stackSpace}");
                    buffer.EmitLine("ret");
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

            buffer.EmitLine("__UnhandledExceptionHandler_Message db 'UNHANDLED EXCEPTION', 10, 0");

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
                        if (inst.C != null)
                        {
                            for (int j = 0; j < inst.C.Count; ++j)
                            {
                                var operand = inst.C[j];
                                var cType = GetOperandDataType(operand);
                                var cOperand = GetOperandString(operand);
                                var argOperand = new Operand { Type = OperandType.Argument, Value = j, Size = operand.Size };
                                if (cType == eCobType.Float)
                                {
                                    if (cType.Size == 32)
                                        buffer.EmitLine($"cvtss2sd {cOperand}, {cOperand}");

                                    buffer.EmitLine($"movsd {GetOperandString(argOperand)}, {cOperand}");
                                }
                                else
                                {
                                    if (argOperand.Size > operand.Size)
                                        buffer.Emit("movzx ");
                                    else
                                        buffer.Emit("mov ");
                                    buffer.EmitLine($"{GetOperandString(argOperand)}, {cOperand}");
                                }
                            }
                        }

                        buffer.Emit("call ");
                        buffer.EmitLine(GetOperandString(inst.A));
                        break;
                    }
                    case Opcode.Return:
                    {
                        // TODO Support type width
                        if (inst.A != null)
                        {
                            buffer.EmitLine($"mov rax, {GetOperandString(inst.A)}");
                        }
                        buffer.EmitLine("jmp .return");
                        break;
                    }
                    case Opcode.Move:
                    {
                        var aType = GetOperandDataType(inst.A);
                        var bType = GetOperandDataType(inst.B);

                        if (bType == eCobType.Float)
                            buffer.Emit(bType.Size == 32 ? "movss " : "movsd ");
                        else if (inst.A.Size > inst.B.Size) // TODO 0 hack
                            buffer.Emit("movzx ");
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
                            // TODO Support different int widths
                            buffer.EmitLine("push rax");
                            buffer.EmitLine("push rbx");
                            buffer.EmitLine($"mov rbx, {GetOperandString(inst.B)}");
                            buffer.EmitLine($"mov rax, {GetOperandString(inst.A)}");
                            buffer.EmitLine("cqo");
                            buffer.EmitLine("idiv rbx");
                            buffer.EmitLine($"mov {GetOperandString(inst.A)}, rax");
                            buffer.EmitLine("pop rbx");
                            buffer.EmitLine("pop rax");
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
                            // TODO Support different int widths
                            buffer.EmitLine("push rax");
                            buffer.EmitLine("push rbx");
                            buffer.EmitLine($"mov rbx, {GetOperandString(inst.B)}");
                            buffer.EmitLine($"mov rax, {GetOperandString(inst.A)}");
                            buffer.EmitLine("cqo");
                            buffer.EmitLine("idiv rbx");
                            buffer.EmitLine($"mov {GetOperandString(inst.A)}, rdx");
                            buffer.EmitLine("pop rbx");
                            buffer.EmitLine("pop rax");
                        }
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
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
                0 => "qword", // TODO Error?
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
                        return GetIntegerRegisterName((int)operand.Value, operand.Size);
                }
                case OperandType.Argument:
                {
                    var idx = operand.Value;
                    if (idx < 4)
                    {
                        // TODO Clean this up
                        var type = typeOverride ?? registerTypes[operand.Value];
                        if (type == eCobType.Float)
                            return GetFloatRegisterName((int)operand.Value);
                        else
                            return GetIntegerRegisterName(parameterRegisters[(int)operand.Value], operand.Size);
                    }

                    return $"{describe} [rsp + {stackSpace + operand.Value * 8 + 8}]";
                }
                case OperandType.Local: // TODO Use size
                    return $"{describe} [rsp + {callReserve + operand.Value * 8}]";
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

        private const int MaxRegisters = 13; // TODO Could have more
        private readonly static int[] parameterRegisters = { 6, 5, 4, 3 }; // rcx, rdx, r8, r9
        private readonly static HashSet<string> nonVolatileRegisters = new ()
        {
            "rbx", "rbp", "rdi", "rsi", "rsp", "r12", "r13", "r14", "r15",
            "xmm6", "xmm7", "xmm8", "xmm9", "xmm10", "xmm11", "xmm12",
            "xmm13", "xmm14", "xmm15"
        };

        // Ordered by least to most likely to need preservation operations
        // rax r10 r11 [r9 r8 rdx rcx] (r12 r13 r14 r15 rbx rdi rsi)
        private readonly static string[,] registers =
        {
            { null,  null,  null,  null, null,  null,  null, null,  null,  null,   null, null,  null  }, // 0
            {  "al", "r10b","r11b","r9b","r8b", "dl",  "cl", "r12b","r13b","r14b", "bl", null,  null  }, // 8
            {  "ax", "r10w","r11w","r9w","r8w", "dx",  "cx", "r12w","r13w","r14w", "bx", null,  null  }, // 16
            { "eax", "r10d","r11d","r9d","r8d","edx", "ecx", "r12d","r13d","r14d","rbx", "rdi", "rsi" }, // 24
            { "eax", "r10d","r11d","r9d","r8d","edx", "ecx", "r12d","r13d","r14d","rbx", "rdi", "rsi" }, // 32
            { "rax", "r10", "r11", "r9", "r8", "rdx", "rcx", "r12", "r13", "r14", "rbx", "rdi", "rsi" }, // 40
            { "rax", "r10", "r11", "r9", "r8", "rdx", "rcx", "r12", "r13", "r14", "rbx", "rdi", "rsi" }, // 48
            { "rax", "r10", "r11", "r9", "r8", "rdx", "rcx", "r12", "r13", "r14", "rbx", "rdi", "rsi" }, // 56
            { "rax", "r10", "r11", "r9", "r8", "rdx", "rcx", "r12", "r13", "r14", "rbx", "rdi", "rsi" }, // 64
        };

        private string GetIntegerRegisterName(int i, int bitSize)
        {
            var j = (bitSize + 7) / 8;
            if (j == 0) j = 8; // TODO Error?
            return registers[j, i];
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
                if (e.Data == null)
                    return;

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
