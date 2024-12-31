namespace Emulator
{
    public sealed class Assembler
    {
        private readonly Dictionary<string, short> labels = new();
        private readonly Dictionary<long, string> fixups = new();
        private Action<long>? resolveFixup1, resolveFixup2;
        private int origin;

        private readonly Dictionary<string, MicrocodeRom.Opcode> opcodeMetadata;

        public Assembler(MicrocodeRom microcodeRom)
        {
            opcodeMetadata = microcodeRom?.OpcodeMetadata ?? throw new ArgumentNullException(nameof(microcodeRom));
        }

        public byte[] AssembleSource(string source)
        {
            var format = OutputFormat.Bin;
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

                string? operand1String = null;
                string? operand2String = null;
                var opcodeString = string.Join("", line.TakeWhile(char.IsLetter)).ToUpperInvariant();
                int j = opcodeString.Length;
                while (j < line.Length && char.IsWhiteSpace(line[j]))
                    ++j;

                if (opcodeString == "FORMAT")
                {
                    var operandString = line[j..];
                    format = Enum.Parse<OutputFormat>(operandString, true);
                    continue;
                }
                else if (opcodeString == "ORIGIN")
                {
                    var operandString = line[j..].ToUpperInvariant();
                    TryParseImm(operandString, -1, out _, out var sOrigin);
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
                                    ch = (char)Convert.ToInt16(operandString.Substring(++j, 2), 16);
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
                        TryParseImm(operandString, -1, out var resultWidth, out var result);
                        if (resultWidth != 1) throw new AssemblyException(i, $"The value {result:X16} does not fit within 1 byte of data.");
                        writer.Write((byte)(result & 0xFF));
                    }
                    continue;
                }
                else if (opcodeString == "DW")
                {
                    var operandString = line[j..];
                    TryParseImm(operandString, -1, out _, out var result);
                    writer.Write((byte)(result >> 8));
                    writer.Write((byte)(result & 0xFF));
                    continue;
                }

                int operandCount = 0;
                var commaIdx = line.IndexOf(',', j);
                if (commaIdx != -1)
                {
                    operandCount = 2;

                    var k = commaIdx + 1;
                    while (k < line.Length && char.IsWhiteSpace(line[k]))
                        ++k;

                    operand2String = line.Substring(k, line.Length - k).ToUpperInvariant();
                }
                else
                {
                    operandCount = 1;
                    commaIdx = line.Length;
                }

                operand1String = line.Substring(j, commaIdx - j).ToUpperInvariant();
                if (operand1String.Length == 0)
                    operandCount = 0;

                resolveFixup1 = resolveFixup2 = null;
                var operand1 = ParseOperand(i, 0, operand1String);
                var operand2 = ParseOperand(i, 1, operand2String);
                
                if (!opcodeMetadata.TryGetValue(opcodeString, out var metadata))
                    throw new AssemblyException(i, $"Unknown opcode '{opcodeString}'");

                var (opcode, operandOrder) = ParseOpcode(i, metadata, operandCount, operand1.Type, operand2.Type);

                Operand? operandB, operandA;
                Action<long>? resolveFixupB, resolveFixupA;
                if (operandCount == 2)
                {
                    if (!operandOrder)
                    {
                        operandA = operand1; resolveFixupA = resolveFixup1;
                        operandB = operand2; resolveFixupB = resolveFixup2;
                    }
                    else if (operandOrder)
                    {
                        operandA = operand2; resolveFixupA = resolveFixup2;
                        operandB = operand1; resolveFixupB = resolveFixup1;
                    }
                    else
                        throw new AssemblyException(i, $"Illegal operand direction specified in metadata '{opcodeString}'");
                }
                else if (operandCount == 1)
                {
                    operandA = operand1; resolveFixupA = resolveFixup1;
                    operandB = null; resolveFixupB = null;
                }
                else
                {
                    operandA = null; resolveFixupA = null;
                    operandB = null; resolveFixupB = null;
                }

                if (operandA != null)
                {
                    if (operandA.Type is OperandType.Reg
                                      or OperandType.DerefByteSegRegPlusSImm or OperandType.DerefWordSegRegPlusSImm
                                      or OperandType.DerefByteSegReg or OperandType.DerefWordSegReg
                                      or OperandType.DerefSegUImm16)
                        opcode |= (ushort)(operandA.Data1 & 0x000F);
                }

                writer.Write((byte)(opcode >> 8));
                if (operandCount > 0)
                    writer.Write((byte)(opcode & 0xFF));

                if (operandA != null)
                {
                    resolveFixupA?.Invoke(stream.Position);

                    ushort data;
                    int width;

                    switch (operandA.Type)
                    {
                        case OperandType.Reg:
                            data = 0;
                            width = 0; // Already encoded in opcode
                            break;
                        case OperandType.Imm8:
                            data = (ushort)operandA.Data1;
                            width = 1;
                            break;
                        case OperandType.Imm16:
                            data = (ushort)operandA.Data1;
                            width = 2;
                            break;
                        case OperandType.DerefByteSegRegPlusSImm:
                        case OperandType.DerefWordSegRegPlusSImm:
                            data = (ushort)operandA.Data2;
                            width = (operandA.Data1 & 0x0C) == 0x04 ? 1 : 2;
                            break;
                        case OperandType.DerefByteSegReg:
                        case OperandType.DerefWordSegReg:
                            data = 0;
                            width = 0;
                            break;
                        case OperandType.DerefSegUImm16:
                            data = (ushort)operandA.Data2;
                            width = 2;
                            break;
                        default:
                            throw new AssemblyException(i, $"Unhandled operandA type {operandA.Type}");
                    }

                    if (width == 1)
                        writer.Write((byte)data);
                    else if (width == 2)
                    {
                        writer.Write((byte)(data >> 8));
                        writer.Write((byte)(data & 0xFF));
                    }
                }

                if (operandB != null)
                {
                    resolveFixupB?.Invoke(stream.Position);

                    ushort data;
                    int width;

                    switch (operandB.Type)
                    {
                        case OperandType.Reg:
                            data = (ushort)operandB.Data1;
                            width = 1;
                            break;
                        case OperandType.Imm8:
                            data = (ushort)operandB.Data1;
                            width = 1;
                            break;
                        case OperandType.Imm16:
                            data = (ushort)operandB.Data1;
                            width = 2;
                            break;
                        case OperandType.DerefByteSegRegPlusSImm:
                        case OperandType.DerefWordSegRegPlusSImm:
                            writer.Write((byte)operandB.Data1);
                            data = (ushort)operandB.Data2;
                            width = (operandB.Data1 & 0x0C) == 0x04 ? 1 : 2;
                            break;
                        case OperandType.DerefByteSegReg:
                        case OperandType.DerefWordSegReg:
                            writer.Write((byte)operandB.Data1);
                            data = 0;
                            width = 0;
                            break;
                        case OperandType.DerefSegUImm16:
                            writer.Write((byte)operandB.Data1);
                            data = (ushort)operandB.Data2;
                            width = 2;
                            break;
                        default:
                            throw new AssemblyException(i, $"Unhandled operandB type {operandB.Type}");
                    }

                    if (width == 1)
                        writer.Write((byte)data);
                    else if (width == 2)
                    {
                        writer.Write((byte)(data >> 8));
                        writer.Write((byte)(data & 0xFF));
                    }
                }
            }

            foreach (var kvp in fixups)
            {
                if (!labels.TryGetValue(kvp.Value, out var address))
                    throw new AssemblyException(-1, $"Reference to undeclared label '{kvp.Value}'");

                stream.Position = kvp.Key;
                var imm16 = (ushort)(origin + address);
                writer.Write((byte)(imm16 >> 8));
                writer.Write((byte)(imm16 & 0xFF));
            }

            return stream.ToArray();
        }

        private (ushort, bool) ParseOpcode(int line, MicrocodeRom.Opcode metadata, int operandCount, OperandType operand1, OperandType operand2)
        {
            if (metadata.OperandCount != operandCount)
                throw new AssemblyException(line, $"Opcode '{metadata.Name}' expected {metadata.OperandCount} operands, received {operandCount} instead");
            
            int result = 0;
            result |= (metadata.Index & 0x3F) << 10;
            result |= ((byte)operand1 & 0x07) << 7;
            result |= ((byte)operand2 & 0x07) << 4;

            if (operandCount > 0)
            {
                var operandCombination = (byte)(((byte)((int)operand1 & 0x07) << 4) | (byte)((int)operand2 & 0x07));
                var operandCombinationIdx =
                    metadata.OperandCombinations.FindIndex(x => (x & 0x7F) == operandCombination);
                if (operandCombinationIdx == -1)
                    throw new AssemblyException(line,
                        $"Opcode '{metadata.Name}' does not support the operand combination {operand1}, {operand2}");

                return ((ushort)result, (metadata.OperandCombinations[operandCombinationIdx] & 0x80) != 0);
            }
            else
                return ((ushort)result, false);
        }

        private Operand ParseOperand(int line, int operandIdx, string? operand)
        {
            short data1, data2;
            
            if (string.IsNullOrEmpty(operand))
                return new Operand(OperandType.None);

            // CHARACTERS
            if (operand.Length >= 2 && operand[0] == '\'')
            {
                // TODO Escape codes '^n
                return new Operand(OperandType.Imm8, (byte)operand[1]);
            }

            // REG
            if ((data1 = ParseRegisterIndex(operand)) != -1)
                return new Operand(OperandType.Reg, data1);
            
            // IMM
            if (TryParseImm(operand, operandIdx, out var data1Width, out data1))
            {
                var imm1Type = data1Width == 1 ? OperandType.Imm8 : OperandType.Imm16;
                return new Operand(imm1Type, data1);
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

                // [SEG:REG] / [SEG:REG+sIMM]
                ParseSegRegIndex(regOperand, out data1, out var data1ImmWidth);
                if (data1 != -1)
                {
                    OperandType operandType;

                    if ((signIdx = indOperand.IndexOfAny(SignChars)) != -1)
                    {
                        // [SEG:REG+sIMM]
                        var sign = indOperand[signIdx] == '-' ? -1 : 1;
                        var numberString = indOperand.Substring(signIdx + 1, indOperand.Length - signIdx - 1);
                        if (!TryParseImm(numberString, operandIdx, out var data2ImmWidth, out data2))
                            throw new AssemblyException(line, $"Illegal operand '{operand}'");
                        if (data2ImmWidth > data1ImmWidth)
                            throw new AssemblyException(line, $"Operand sImm would overflow {data2ImmWidth} > {data1ImmWidth}");
                        
                        operandType = isByte ? OperandType.DerefByteSegRegPlusSImm : OperandType.DerefWordSegRegPlusSImm;
                        data2 = (short)(data2 * -sign); // Negative sign because we SUB for sign purposes
                    }
                    else
                    {
                        // [SEG:REG]
                        operandType = isByte ? OperandType.DerefByteSegReg : OperandType.DerefWordSegReg;
                        data2 = 0;
                    }

                    return new Operand(operandType, data1, data2);
                }

                // [SEG:uIMM16]
                int colonIdx = operand.IndexOf(':');
                var segOperand = operand[1..colonIdx];
                var immOperand = operand[(colonIdx + 1)..^1];
                data1 = ParseSegmentIndex(segOperand);

                if (TryParseImm(immOperand, operandIdx, out _, out data2))
                {
                    if (isByte)
                        data1 = (short)((data1 & 0x03) | 0x04);
                    return new Operand(OperandType.DerefSegUImm16, data1, data2);
                }
            }

            throw new AssemblyException(line, $"Illegal operand '{operand}'");
        }

        private readonly static string[] Registers =
        {
            "R0", "R1", "R2", "R3", "SP", "SS", "CS", "DS",
            "R0H", "R0L", "R1H", "R1L", "R2H", "R2L", "R3H", "R3L"
        };
        private static short ParseRegisterIndex(string registerName)
        {
            return (short)Array.IndexOf(Registers, registerName);
        }
        private readonly static string[] SegRegs =
        {
            "DS:R0", "DS:R1", "DS:R2", "DS:R3", "SS:SP", "SS:R1", "CS:R2", "DS:R3",
            "SS:R0", "CS:R0", "0XE000:R1", "0XC000:R1", "0X8000:R2", "0X4000:R2", "0X2000:R3", "0X0000:R3"
        };
        private static void ParseSegRegIndex(string registerName, out short idx, out int width)
        {
            idx = (short)Array.IndexOf(SegRegs, registerName);
            width = (idx & 0x0C) == 0x04 ? 1 : 2;
        }
        private readonly static string[] Segments =
        {
            "DS", "DS", "DS", "DS", "SS", "SS", "CS", "DS",
            "SS", "CS", "0XE000", "0XC000", "0X8000", "0X4000", "0X2000", "0X0000"
        };
        private static short ParseSegmentIndex(string registerName)
        {
            return (short)Array.IndexOf(Segments, registerName);
        }

        private readonly static char[] SignChars = { '+', '-' };
        private bool TryParseImm(string value, int operandIdx, out int resultWidth, out short result)
        {
            int sign = value[0] == '-' ? -1 : 1;
            if (value[0] is '+' or '-')
                value = value[1..];

            if (value.All(char.IsDigit))
            {
                var uResult = short.Parse(value);
                resultWidth = uResult <= 0xFF ? 1 : 2;
                result = (short)(uResult * sign);
                return true;
            }

            if (value.Length >= 3 && value[0] == '0' && value[1] is 'x' or 'X'
            &&  value.Skip(2).All(IsHexDigit))
            {
                var uResult = Convert.ToInt16(value[2..], 16);
                resultWidth = uResult <= 0xFF ? 1 : 2;
                result = (short)(uResult * sign);
                return true;
            }

            if ((char.IsLetter(value[0]) || value[0] == '_')
            &&  !value.StartsWith("WORD") && !value.StartsWith("BYTE"))
            {
                if (sign == -1) throw new AssemblyException(-1, $"Label '{value}' cannot be negatively addressed");

                if (labels.TryGetValue(value, out result))
                {
                    result += (short)origin;
                    resultWidth = 2; // TODO Wrong??
                    return true;
                }

                void Fixup(long x) => fixups.Add(x, value);
                if (operandIdx == 0)
                    resolveFixup1 = Fixup;
                else if (operandIdx == 1)
                    resolveFixup2 = Fixup;
                else if (operandIdx != -1)
                    throw new ArgumentOutOfRangeException(nameof(operandIdx), operandIdx, "Must be 0 or 1");
                
                result = -1;
                resultWidth = 0; // TODO Wrong??
                return true;
            }

            result = 0;
            resultWidth = 0;
            return false;
        }

        private static bool IsHexDigit(char ch)
        {
            return char.IsDigit(ch)
                || (ch >= 'A' && ch <= 'F')
                || (ch >= 'a' && ch <= 'f');
        }

        private sealed record Operand(OperandType Type, short Data1 = 0, short Data2 = 0);

        private enum OutputFormat
        {
            Bin,
            Exe
        }
    }
}
