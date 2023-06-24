using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Emulator
{
    internal static class MicrocodeAssembler
    {
        public const int InstructionWidth = 16;
        public const int ControlWordWidth = 24;

        public static void CompileFile(string path, string outputPath)
        {
            var macros = new Dictionary<string, Procedure>();
            var opcodes = new Dictionary<int, Procedure>();

            var lines = File.ReadAllLines(path);
            Procedure? current = null;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                
                var semiIdx = line.IndexOf(';');
                if (semiIdx >= 0)
                    line = line[..semiIdx];

                line = line.Trim();

                if (string.IsNullOrEmpty(line))
                    continue;

                var parts = line.Split(' ');
                if (current == null)
                {
                    // MACRO DECLARATION
                    if (parts[0] == "#")
                    {
                        var macroName = parts[1];
                        if (macros.ContainsKey(macroName))
                            throw new AssemblyException(i, $"Macro '{macroName}' already declared");

                        current = new Procedure(macroName, false);
                        macros[macroName] = current;
                        continue;
                    }

                    // OPCODE DECLARATION
                    int opcode = 0;

                    var operandCount = int.Parse(parts[0]);
                    var opcodeIndex = Convert.ToInt32(parts[1], 2) & 0x1F;
                    var opcodeName = parts[2];
                    var isWildcard = false;

                    opcode |= opcodeIndex << 10;

                    if (operandCount == 1)
                    {
                        var operand1 = ParseOperand(parts[3], i);
                        var hasZF = parts.Contains("+ZF") ? 0x0200 : 0;
                        var hasCF = parts.Contains("+CF") ? 0x0100 : 0;
                        var hasSF = parts.Contains("+SF") ? 0x0080 : 0;
                        isWildcard = parts[4] == "*";
                        
                        opcode |= 0x8000;
                        opcode |= (int)operand1 << 7;
                        if (!isWildcard)
                        {
                            opcode |= hasZF;
                            opcode |= hasCF;
                            opcode |= hasSF;
                        }
                    }
                    else if (operandCount == 2)
                    {
                        var operand1 = ParseOperand(parts[3], i);
                        var operand2 = ParseOperand(parts[4], i);

                        opcode |= (int)operand1 << 7;
                        opcode |= (int)operand2 << 4;
                    }
                    else if (operandCount != 0)
                        throw new AssemblyException(i, $"Illegal operand count {operandCount}");

                    current = new Procedure(opcodeName, isWildcard);

                    if (isWildcard)
                    {
                        for (int j = 0; j < 8; ++j)
                        {
                            var wcOpcode = opcode | (j << 4);
                            if (opcodes.TryGetValue(wcOpcode, out var existing) && !existing.IsWildcard)
                                throw new AssemblyException(i, $"Opcode '{opcodeName}' is already declared without wildcard");

                            opcodes[wcOpcode] = current;
                        }
                    }
                    else
                    {
                        if (opcodes.TryGetValue(opcode, out var existing) && !existing.IsWildcard)
                            throw new AssemblyException(i, $"Opcode '{opcodeName}' is already declared without wildcard");

                        opcodes[opcode] = current;
                    }
                }
                else
                {
                    var word = ControlWord.None;
                    foreach (var part in parts)
                    {
                        if (part == "END")
                        {
                            current = null;
                            break;
                        }

                        if (part[0] == '#')
                        {
                            if (parts.Length > 1)
                                throw new AssemblyException(i, "Macro cannot be mixed in with control word");

                            var macroName = part[1..];
                            if (!macros.TryGetValue(macroName, out var macro))
                                throw new AssemblyException(i, $"Unknown macro '{macroName}'");

                            var macroCode = macro.Code;
                            var macroCodeLength = macro.CodeLength;
                            for (int j = 0; j < macroCodeLength; ++j)
                            {
                                var k = current.CodeLength++;
                                if (k >= Procedure.MaxMicrocodeCount)
                                    throw new AssemblyException(k, $"Microcode exceeds {Procedure.MaxMicrocodeCount} words");
                                
                                current.Code[k] = macroCode[j];
                            }

                            break;
                        }

                        var subParts = part.Split(':');
                        for (var j = 0; j < subParts.Length; ++j)
                        {
                            var subPart = subParts[j];
                            if (subPart[0] == '$')
                            {
                                subPart = subPart[1..];
                                word |= ControlWord.ADDR;
                            }

                            // TODO Validate word
                            word |= Enum.Parse<ControlWord>(subPart);
                        }
                    }

                    if (current == null)
                        continue; // END

                    var l = current.CodeLength++;
                    if (l >= Procedure.MaxMicrocodeCount)
                        throw new AssemblyException(l, $"Microcode exceeds {Procedure.MaxMicrocodeCount} words");
                                
                    current.Code[l] = word;
                }
            }

            //var result = new byte[(int)Math.Pow(2, InstructionWidth) * ControlWordWidth];
            var result = new byte[0xFFFF * 3];
            foreach (var opcode in opcodes)
            {
                var addr = opcode.Key;
                var code = opcode.Value.Code;
                var codeLength = opcode.Value.CodeLength;
                for (int i = 0; i < codeLength; ++i)
                {
                    var romAddr = (addr | i) * 3;
                    result[romAddr + 0] = (byte)((int)code[i] & 0xFF);
                    result[romAddr + 1] = (byte)(((int)code[i] >> 8) & 0xFF);
                    result[romAddr + 2] = (byte)(((int)code[i] >> 16) & 0xFF);

                    //var romAddr = (addr | i);
                    //result[romAddr + 0x00000] = (byte)((int)code[i] & 0xFF);
                    //result[romAddr + 0x0FFFF] = (byte)(((int)code[i] >> 8) & 0xFF);
                    //result[romAddr + 0x1FFFE] = (byte)(((int)code[i] >> 16) & 0xFF);
                }
            }

            File.WriteAllBytes(outputPath, result);
        }

        private static Operand ParseOperand(string value, int line)
        {
            return value switch
            {
                "REG" => Operand.Reg,
                "IMM16" => Operand.Imm16,
                "[REG+IMM16]" => Operand.DerefRegPlusImm16,
                "[IMM16]" => Operand.DerefImm16,
                _ => throw new AssemblyException(line, $"Illegal operand: {value}")
            };
        }

        private enum Operand
        {
            None,
            Reg = 2,
            Imm16,
            DerefRegPlusImm16,
            DerefImm16
        }

        private sealed class Procedure
        {
            public const int MaxMicrocodeCount = 16;

            public string Name { get; }

            public bool IsWildcard { get; }

            public ControlWord[] Code { get; }

            public int CodeLength { get; set; }

            public Procedure(string name, bool isWildcard)
            {
                Name = name;
                IsWildcard = isWildcard;
                Code = new ControlWord[MaxMicrocodeCount];
                CodeLength = 0;
            }
        }
    }

    public sealed class AssemblyException : Exception
    {
        public int Line { get; }
            
        public AssemblyException(int line, string message)
            : base(message)
        {
            Line = line;
        }
    }
}
