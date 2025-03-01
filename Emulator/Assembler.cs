using System.Collections;
using System.Numerics;

namespace Emulator
{
    public sealed class Assembler
    {
        private readonly Dictionary<string, short> labels = new();
        private readonly Dictionary<long, (string, bool)> fixups = new();
        private int origin;

        private readonly Dictionary<string, MicrocodeRom.Opcode> opcodeMetadata;

        public Assembler(MicrocodeRom microcodeRom)
        {
            opcodeMetadata = microcodeRom?.OpcodeMetadata ?? throw new ArgumentNullException(nameof(microcodeRom));
        }

        public byte[] AssembleSource(string source)
        {
            var outputFormat = OutputFormat.Bin;
            origin = 0;

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            labels.Clear();
            fixups.Clear();

            var lines = source.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                var semiIdx = line.IndexOf(';');
                if (semiIdx >= 0)
                    line = line[..semiIdx];

                line = line.Trim();

                var colonIdx = line.IndexOf(':');
                if (colonIdx >= 0)
                {
                    var labelName = line[..colonIdx].ToUpperInvariant().TrimStart();

                    var isSubLabel = false;
                    var isLabel = true;
                    for (int k = 0; isLabel && k < labelName.Length; ++k)
                    {
                        var ch = labelName[k];
                        if (k == 0 && ch == '.')
                            isSubLabel = true;
                        else if (!char.IsLetterOrDigit(ch) && ch != '_')
                            isLabel = false;
                    }

                    if (isLabel)
                    {
                        line = line[(colonIdx + 1)..];
                        line = line.Trim();

                        // TODO sublabels
                        if (labels.ContainsKey(labelName))
                            throw new AssemblyException(i, $"Redeclaration of label '{labelName}'");

                        // TODO Gotta find a better way to approach this because Cobalt technically supports
                        //      a 32bit address space, but we can only page 16bits of it at a time...
                        if (stream.Position > ushort.MaxValue)
                            throw new AssemblyException(i, $"Label '{labelName}' would overflow address space");

                        labels.Add(labelName, (short)stream.Position);
                    }
                }

                if (string.IsNullOrEmpty(line))
                    continue;

                Conditional conditional;
                var opcodeString = string.Join("", line.TakeWhile(char.IsLetter)).ToUpperInvariant();
                int j = opcodeString.Length;

                if (j < line.Length && line[j] == '.')
                {
                    var conditionalString = string.Join("", line.Skip(j + 1).TakeWhile(char.IsLetter)).ToUpperInvariant();
                    conditional = Enum.Parse<Conditional>(conditionalString, true);
                    j += conditionalString.Length + 1;
                }
                else
                    conditional = Conditional.None;

                while (j < line.Length && char.IsWhiteSpace(line[j]))
                    ++j;

                if (opcodeString == "FORMAT")
                {
                    var operandString = line[j..];
                    outputFormat = Enum.Parse<OutputFormat>(operandString, true);
                    continue;
                }
                else if (opcodeString == "ORIGIN")
                {
                    var operandString = line[j..].ToUpperInvariant();
                    TryParseImm(operandString, out var sOrigin, out _);
                    origin = sOrigin & 0xFFFF;
                    continue;
                }
                else if (opcodeString == "DB")
                {
                    var operandString = line;
                    if (operandString[j] == '"' && operandString[^1] == '"')
                    {
                        while (++j < operandString.Length - 1)
                        {
                            var ch = operandString[j];
                            if (ch == '^')
                            {
                                var escapeCode = operandString[++j];
                                if (escapeCode == 'x')
                                {
                                    ch = (char)Convert.ToInt16(operandString.Substring(j + 1, 2), 16);
                                    j += 2;
                                }
                                else
                                {
                                    ch = escapeCode switch
                                    {
                                        '0' => '\0',
                                        'b' => '\b',
                                        'r' => '\r',
                                        'n' => '\n',
                                        't' => '\t',
                                        '"' => '\"',
                                        '^' => '^',
                                        _ => throw new Exception($"Illegal escape code '{operandString[j]}'")
                                    };
                                }
                            }

                            writer.Write((byte)ch);
                        }
                    }
                    else
                    {
                        operandString = operandString[j..];
                        if (!TryParseImm(operandString, out var result, out var fixup)
                        ||  (result & 0xFF00) is not (0xFF or 0x00))
                            throw new AssemblyException(i, $"The value {result:X16} does not fit within 1 byte of data.");
                        fixup?.Invoke(stream.Position, null);
                        writer.Write((byte)(result & 0xFF));
                    }
                    continue;
                }
                else if (opcodeString == "DW")
                {
                    while (j < line.Length)
                    {
                        var l = line.IndexOf(',', j);
                        if (l == -1) l = line.Length;

                        var operandString = line.Substring(j, l - j).Trim().ToUpperInvariant();
                        TryParseImm(operandString, out var result, out var fixup);
                        fixup?.Invoke(stream.Position, null); // TODO Clean this up

                        writer.Write((byte)(result >> 8));
                        writer.Write((byte)(result & 0xFF));

                        j = l + 1;
                    }

                    continue;
                }
                else if (opcodeString == "JMP") // JMP SHORT, JMP LONG
                {
                    // HACK Kinda dumb, but I like the syntax
                    opcodeString = line.IndexOf(',', j) == -1 ? "JMPS" : "JMPL";
                }
                
                if (!opcodeMetadata.TryGetValue(opcodeString, out var metadata))
                    throw new AssemblyException(i, $"Unknown opcode '{opcodeString}'");

                var operands = new List<Operand>();

                while (j < line.Length)
                {
                    var l = line.IndexOf(',', j);
                    if (l == -1) l = line.Length;

                    var operandString = line.Substring(j, l - j).Trim().ToUpperInvariant();
                    var operand = ParseOperand(i, operandString);
                    operands.Add(operand);

                    j = l + 1;
                }

                if (metadata.OperandCount != operands.Count)
                    throw new AssemblyException(i, $"Opcode '{metadata.Name}' expected {metadata.OperandCount} operands, received {operands.Count} instead");

                // TOOOCCCC OOSIIFFF
                var operandTypes = operands.Select(x => x.Type switch
                {
                    OperandType.None => OperandType.None,
                    OperandType.Reg => OperandType.Reg,
                    OperandType.Imm => OperandType.Imm,
                    OperandType.DerefSizePgRegPlusSImm => OperandType.DerefSizePgRegPlusSImm,
                    OperandType.DerefSizePgReg => OperandType.DerefSizePgReg,
                    OperandType.DerefSizePgUImm => OperandType.DerefSizePgUImm,
                    OperandType.DerefBytePgRegPlusSImm => OperandType.DerefSizePgRegPlusSImm,
                    OperandType.DerefWordPgRegPlusSImm => OperandType.DerefSizePgRegPlusSImm,
                    OperandType.DerefBytePgReg => OperandType.DerefSizePgReg,
                    OperandType.DerefWordPgReg => OperandType.DerefSizePgReg,
                    OperandType.DerefBytePgUImm => OperandType.DerefSizePgUImm,
                    OperandType.DerefWordPgUImm => OperandType.DerefSizePgUImm,
                    _ => throw new ArgumentOutOfRangeException()
                }).ToList();
                var operandCombo = metadata.OperandCombinations.FirstOrDefault(x => x.SequenceEqual(operandTypes));
                if (operandCombo == null)
                    throw new AssemblyException(i, $"Opcode '{metadata.Name}' does not support the operand combination {string.Join(' ', operands)}");
                if (operands.Count == 0 && conditional != Conditional.None)
                    throw new AssemblyException(i, $"Opcode '{metadata.Name}' does not support flags");
                
                var iS = -1;
                var immALength = 0;
                var immBLength = 0;
                foreach (var operand in operands)
                {
                    if (IsImmRefOperand(operand))
                    {
                        if (immALength == 0)
                            immALength = GetImmSizeBits(operand.ImmValue);
                        else if (immBLength == 0)
                            immBLength = GetImmSizeBits(operand.ImmValue);
                    }

                    var sz = operand.Type switch
                    {
                        OperandType.Reg => -1,
                        OperandType.Imm => -1,
                        OperandType.DerefBytePgRegPlusSImm => 0,
                        OperandType.DerefWordPgRegPlusSImm => 1,
                        OperandType.DerefBytePgReg => 0,
                        OperandType.DerefWordPgReg => 1,
                        OperandType.DerefBytePgUImm => 0,
                        OperandType.DerefWordPgUImm => 1,
                        _ => throw new ArgumentOutOfRangeException(nameof(operand.Type), operand.Type, "Illegal Operand Type")
                    };

                    if (sz != -1)
                    {
                        if (iS == -1)
                            iS = sz;
                        else if (sz != iS)
                            throw new AssemblyException(i, "Cannot mix byte and word addressing modes");
                    }
                }

                iS = Math.Clamp(iS, 0, 1);

                var operandIndex = 0;
                var operandIndices = Microcode.ResolveOperandIndices(operandTypes);
                if (operandIndices != null)
                {
                    int fmtImmALen = int.MaxValue;
                    int fmtImmBLen = int.MaxValue;
                    foreach (var lOperandIndex in operandIndices)
                    {
                        var formatIndex = lOperandIndex & 0x07;
                        var operandFormat = Microcode.OperandFormats[formatIndex];
                        if (operandFormat.LengthA < fmtImmALen && operandFormat.LengthA >= immALength
                        &&  operandFormat.LengthB < fmtImmBLen && operandFormat.LengthB >= immBLength)
                        {
                            operandIndex = lOperandIndex;
                            fmtImmALen = operandFormat.LengthA;
                            fmtImmBLen = operandFormat.LengthB;
                        }
                    }
                }

                var ba = new BitArray(48);
                
                var iT = conditional == Conditional.None ? 1 : 0;
                if (operands.Count == 0)
                    ba.Write(1, 3, metadata.Index);
                else
                {
                    ba.Write(0, 1, iT);
                    ba.Write(1, 3, (metadata.Index & 0x1C) >> 2);
                    ba.Write(4, 4, (int)conditional);
                    ba.Write(8, 2, metadata.Index & 0x03);
                    ba.Write(10, 1, iS);
                    ba.Write(11, 5, operandIndex);
                }

                int regIndex = 0, immIndex = 0;
                for (int k = 0; k < operands.Count; ++k)
                {
                    var operand = operands[k];

                    if (IsRegRefOperand(operand))
                    {
                        var idx = (iT << 4) | regIndex;
                        var offset = idx switch
                        {
                            0x00 => 16,
                            0x01 => 20,
                            0x02 => 24,
                            0x10 => 4,
                            0x11 => 16,
                            0x12 => 20,
                            _ => throw new ArgumentOutOfRangeException(nameof(idx), idx, "Illegal RegIndex")
                        };

                        ba.Write(offset, 4, operand.RegIndex);
                        ++regIndex;
                    }

                    if (IsImmRefOperand(operand))
                    {
                        var operandFormat = Microcode.OperandFormats[operandIndex & 0x07];
                        int offset, length;
                        if (immIndex == 0)
                        {
                            offset = operandFormat.OffsetA;
                            length = operandFormat.LengthA;
                        }
                        else if (immIndex == 1)
                        {
                            offset = operandFormat.OffsetB;
                            length = operandFormat.LengthB;
                        }
                        else
                            throw new ArgumentOutOfRangeException(nameof(immIndex), immIndex, "Illegal ImmIndex");

                        operand.Fixup?.Invoke(stream.Position + offset / 8, operand); // TODO unaligned write
                        ba.Write(offset, length, operand.ImmValue);
                        ++immIndex;
                    }
                }

                var size = Microcode.ResolveEncodedInstructionSizeInBytes(operandTypes, operandIndex);
                var inst = new byte[48 / 8];
                ba.CopyTo(inst, 0);
                Array.Reverse(inst);
                writer.Write(inst, 0, size);
            }

            foreach (var kvp in fixups)
            {
                if (!labels.TryGetValue(kvp.Value.Item1, out var address))
                    throw new AssemblyException(-1, $"Reference to undeclared label '{kvp.Value}'");

                address = (short)(kvp.Value.Item2 ? -address : address);

                stream.Position = kvp.Key;
                var imm16 = (ushort)(origin + address);
                writer.Write((byte)(imm16 >> 8));
                writer.Write((byte)(imm16 & 0xFF));
            }

            return stream.ToArray();
        }

        private Operand ParseOperand(int line, string? operand)
        {
            Action<long, Operand?>? fixup;
            short regIndex, immValue;
            
            if (string.IsNullOrEmpty(operand))
                return new Operand(OperandType.None);

            // CHARACTERS
            if (operand.Length >= 2 && operand[0] == '\'')
            {
                // TODO Escape codes '^n
                return new Operand(OperandType.Imm, ImmValue: (byte)operand[1]);
            }

            // REG
            if ((regIndex = ParseRegisterIndex(operand)) != -1)
                return new Operand(OperandType.Reg, RegIndex: regIndex);
            
            // IMM
            if (TryParseImm(operand, out immValue, out fixup))
            {
                return new Operand(OperandType.Imm, ImmValue: immValue, Fixup: fixup);
            }

            // [REG+IMM] / [IMM]
            bool isByte;
            if (operand.StartsWith("BYTE"))
            {
                operand = operand[4..];
                isByte = true;
            }
            else if (operand.StartsWith("WORD"))
            {
                operand = operand[4..];
                isByte = false;
            }
            else
                isByte = false;
            while (operand[0] is ' ' or '\t')
                operand = operand[1..];
            
            if (operand.Length >= 2 && operand[0] == '[' && operand[^1] == ']')
            {
                int signIdx = operand.IndexOfAny(SignChars);
                var indOperand = operand.Substring(1, operand.Length - 2);
                var regOperand = operand.Substring(1, signIdx != -1 ? signIdx - 1 : operand.Length - 2);

                // [PG:REG] / [PG:REG+sIMM]
                regIndex = ParsePgRegIndex(regOperand);
                if (regIndex != -1)
                {
                    OperandType operandType;

                    if ((signIdx = indOperand.IndexOfAny(SignChars)) != -1)
                    {
                        // [PG:REG+sIMM]
                        var sign = indOperand[signIdx] == '-' ? -1 : 1;
                        var numberString = indOperand.Substring(signIdx + 1, indOperand.Length - signIdx - 1);
                        if (!TryParseImm(numberString, out immValue, out fixup))
                            throw new AssemblyException(line, $"Illegal operand '{operand}'");
                        
                        operandType = isByte ? OperandType.DerefBytePgRegPlusSImm : OperandType.DerefWordPgRegPlusSImm;
                        immValue = (short)(immValue * -sign); // Negative sign because we SUB for sign purposes
                    }
                    else
                    {
                        // [PG:REG]
                        operandType = isByte ? OperandType.DerefBytePgReg : OperandType.DerefWordPgReg;
                        immValue = 0;
                    }

                    return new Operand(operandType, regIndex, immValue);
                }

                // [PG:uIMM]
                int colonIdx = operand.IndexOf(':');
                var pagOperand = operand[1..colonIdx];
                var immOperand = operand[(colonIdx + 1)..^1];
                regIndex = ParsePageIndex(pagOperand, isByte);

                if (regIndex == -1)
                    throw new AssemblyException(line, $"Illegal Addressing Mode for PG:REG '{operand}'");

                if (TryParseImm(immOperand, out immValue, out fixup))
                    return new Operand(OperandType.DerefBytePgUImm, regIndex, immValue, fixup);
            }

            throw new AssemblyException(line, $"Illegal operand '{operand}'");
        }

        private readonly static string[] Registers =
        {
            "R0", "R1", "R2", "R3", "R4", "R5", "R6", "R7",
            "SP", "SG", "CG", "DG", "TG", "R0L", "R0H", "R1L"
        };
        private static short ParseRegisterIndex(string registerName)
        {
            return (short)Array.IndexOf(Registers, registerName);
        }

        private readonly static string[] PgRegs =
        {
            "DG:R0", "DG:R1", "DG:R2", "DG:R3", "DG:R4", "SG:R5", "TG:R6", "CG:R7",
            "SG:SP", "SG:R1", "0XE000:R5", "0XC000:R5", "0X8000:R6", "0X4000:R6", "0X2000:R7", "0X0000:R7"
        };
        private static short ParsePgRegIndex(string registerName)
        {
            return (short)Array.IndexOf(PgRegs, registerName);
        }

        private readonly static string[] ByteAddressingPages =
        {
            "??", "??", "??", "??", "DG", "DG", "TG", "CG",
            "??", "??", "??", "??", "??", "??", "??", "??"
        };
        private readonly static string[] WordAddressingPages =
        {
            "DG", "DG", "DG", "DG", "??", "??", "??", "??",
            "SG", "SG", "0XE000", "0XC000", "0X8000", "0X4000", "0X2000", "0X0000"
        };
        private static short ParsePageIndex(string registerName, bool isByte)
        {
            return isByte
                ? (short)Array.IndexOf(ByteAddressingPages, registerName)
                : (short)Array.IndexOf(WordAddressingPages, registerName);
        }

        private readonly static char[] SignChars = { '+', '-' };
        private bool TryParseImm(string value, out short result, out Action<long, Operand?>? fixup)
        {
            int sign = value[0] == '-' ? -1 : 1;
            if (value[0] is '+' or '-')
                value = value[1..];

            if (value.All(char.IsDigit))
            {
                var uResult = ushort.Parse(value);
                result = (short)(uResult * sign);
                fixup = null;
                return true;
            }

            if (value.Length >= 3 && value[0] == '0' && value[1] is 'x' or 'X'
            &&  value.Skip(2).All(IsHexDigit))
            {
                var uResult = (ushort)Convert.ToInt16(value[2..], 16);
                result = (short)(uResult * sign);
                fixup = null;
                return true;
            }

            if ((char.IsLetter(value[0]) || value[0] == '_')
            &&  !value.StartsWith("WORD") && !value.StartsWith("BYTE"))
            {
                if (sign == -1) throw new AssemblyException(-1, $"Label '{value}' cannot be negatively addressed");

                if (labels.TryGetValue(value, out result))
                {
                    result += (short)origin;
                    fixup = null;
                    return true;
                }

                // TODO Clean this up...
                fixup = (x, operand) => fixups.Add(x, (value, IsNegImmRefOperand(operand)));
                
                result = -1;
                return true;
            }

            result = 0;
            fixup = null;
            return false;
        }

        private static bool IsHexDigit(char ch)
        {
            return char.IsDigit(ch)
                || (ch >= 'A' && ch <= 'F')
                || (ch >= 'a' && ch <= 'f');
        }

        private static bool IsRegRefOperand(Operand? operand)
        {
            return operand != null && operand.Type
                is OperandType.Reg
                or OperandType.DerefSizePgRegPlusSImm
                or OperandType.DerefSizePgReg
                or OperandType.DerefSizePgUImm;
        }

        private static bool IsImmRefOperand(Operand? operand)
        {
            return operand != null && operand.Type
                is OperandType.Imm
                or OperandType.DerefSizePgRegPlusSImm
                or OperandType.DerefSizePgUImm;
        }

        private static bool IsNegImmRefOperand(Operand? operand)
        {
            return operand != null && operand.Type
                is OperandType.DerefSizePgRegPlusSImm;
        }

        private static int GetImmSizeBits(short value)
        {
            if (value == 0)
                return 0;

            return BitOperations.Log2((uint)(value & 0xFFFF)) + 1;
        }

        private sealed record Operand(
            OperandType Type,
            short RegIndex = 0,
            short ImmValue = 0,
            Action<long, Operand?>? Fixup = null
        );

        private enum OutputFormat
        {
            Bin,
            Exe
        }
    }

    internal static class BitArrayUtility
    {
        public static void Write(this BitArray array, int offset, int length, int value)
        {
            for (int i = 0; i < length; ++i)
            {
                array.Set(
                    array.Length - 1 - offset - i,
                    (value & (1 << (length - 1 - i))) != 0
                );
            }
        }

        public static void Write(this BitArray array, int offset, bool value)
        {
            array.Set(array.Length - 1 - offset, value);
        }
    }
}
