namespace Emulator
{
    public sealed class Assembler
    {
        private readonly Dictionary<string, short> labels = new();
        private readonly Dictionary<long, string> fixups = new();
        private Action<long>? resolveFixup1, resolveFixup2, resolveFixup3;
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

                Conditional conditional;
                var opcodeString = string.Join("", line.TakeWhile(char.IsLetter)).ToUpperInvariant();
                int j = opcodeString.Length;

                if (j < line.Length && line[j] == '.')
                {
                    var conditionalString = string.Join("", line.Skip(j + 1).TakeWhile(char.IsLetter)).ToUpperInvariant();
                    conditional = Enum.Parse<Conditional>(conditionalString);
                    j += conditionalString.Length + 1;
                }
                else
                    conditional = Conditional.None;

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
                
                if (!opcodeMetadata.TryGetValue(opcodeString, out var metadata))
                    throw new AssemblyException(i, $"Unknown opcode '{opcodeString}'");

                Operand? operand1 = null;
                Operand? operand2 = null;
                Operand? operand3 = null;
                int operandCount = 0;

                resolveFixup1 = resolveFixup2 = resolveFixup3 = null;

                while (j < line.Length)
                {
                    var l = line.IndexOf(',', j);
                    if (l == -1) l = line.Length;

                    var operandString = line.Substring(j, l - j).Trim().ToUpperInvariant();
                    if (operandCount == 0)
                        operand1 = ParseOperand(i, operandCount, operandString);
                    else if (operandCount == 1)
                        operand2 = ParseOperand(i, operandCount, operandString);
                    else if (operandCount == 2)
                        operand3 = ParseOperand(i, operandCount, operandString);
                    ++operandCount;

                    j = l + 1;
                }

                if (metadata.OperandCount != operandCount)
                    throw new AssemblyException(i, $"Opcode '{metadata.Name}' expected {metadata.OperandCount} operands, received {operandCount} instead");

                var aType = operand1?.Type ?? OperandType.None;
                var bType = operand3?.Type ?? operand2?.Type ?? OperandType.None;
                
                bool operandOrder;
                if (operandCount > 0)
                {
                    var operandCombo = metadata.OperandCombinations.FirstOrDefault(x => x.A == aType && x.B == bType);
                    if (operandCombo == null)
                        throw new AssemblyException(i, $"Opcode '{metadata.Name}' does not support the operand combination {operand1}, {operand2}");

                    operandOrder = operandCombo.RTL;
                }
                else
                    operandOrder = false;

                Operand? operandA, operandB, operandC;
                Action<long>? resolveFixupC, resolveFixupB, resolveFixupA;
                if (operandCount == 3)
                {
                    if (!operandOrder)
                    {
                        operandA = operand1; resolveFixupA = resolveFixup1;
                        operandB = operand2; resolveFixupB = resolveFixup2;
                        operandC = operand3; resolveFixupC = resolveFixup3;
                    }
                    else
                    {
                        operandA = operand3; resolveFixupA = resolveFixup3;
                        operandB = operand2; resolveFixupB = resolveFixup2;
                        operandC = operand1; resolveFixupC = resolveFixup1;
                    }
                }
                else if (operandCount == 2)
                {
                    if (!operandOrder)
                    {
                        operandA = operand1; resolveFixupA = resolveFixup1;
                        operandB = operand2; resolveFixupB = resolveFixup2;
                        operandC = null; resolveFixupC = null;
                    }
                    else
                    {
                        operandA = operand2; resolveFixupA = resolveFixup2;
                        operandB = operand1; resolveFixupB = resolveFixup1;
                        operandC = null; resolveFixupC = null;
                    }
                }
                else if (operandCount == 1)
                {
                    operandA = operand1; resolveFixupA = resolveFixup1;
                    operandB = null; resolveFixupB = null;
                    operandC = null; resolveFixupC = null;
                }
                else
                {
                    operandA = null; resolveFixupA = null;
                    operandB = null; resolveFixupB = null;
                    operandC = null; resolveFixupC = null;
                }

                // ENCODE


                if (operandCount == 0)
                {
                    writer.Write((byte)(((metadata.Index & 0x1C) << 2) | ((byte)conditional & 0x0F)));
                }
                else
                {
                    var opcode = (ushort)(
                          ((metadata.Index & 0x3F) << 10)
                        | (((byte)aType & 0x07) << 3)
                        |  ((byte)bType & 0x07)
                    );

                    var ii = (ushort)((opcode & 0xFC3F) | ((byte)conditional & 0x0F) << 6);
                    writer.Write((byte)(ii >> 8));
                    writer.Write((byte)(ii & 0xFF));
                }

                if (IsRegRefOperand(operandA) || IsRegRefOperand(operandB) || IsRegRefOperand(operandC))
                {
                    byte rd0 = 0x00, rd1 = 0x00;
                    if (operandA != null) rd0 |= (byte)((operandA.Data1 & 0x0F) << 4);
                    if (operandB != null) rd0 |= (byte)(operandB.Data1 & 0x0F);
                    if (operandA != null || operandB != null) writer.Write(rd0);
                    if (operandC != null) rd1 |= (byte)((operandC.Data1 & 0x0F) << 4);
                    if (operandC != null) writer.Write(rd1);
                }

                if (operandA != null)
                {
                    resolveFixupA?.Invoke(stream.Position);

                    ushort data;
                    int width;

                    switch (operandA.Type)
                    {
                        case OperandType.Reg:
                            data = 0;
                            width = 0; // Already encoded
                            break;
                        case OperandType.Imm8:
                            data = (ushort)operandA.Data1;
                            width = 1;
                            break;
                        case OperandType.Imm16:
                            data = (ushort)operandA.Data1;
                            width = 2;
                            break;
                        case OperandType.DerefBytePgRegPlusSImm:
                        case OperandType.DerefWordPgRegPlusSImm:
                            data = (ushort)operandA.Data2;
                            width = (operandA.Data1 & 0x0C) == 0x04 ? 1 : 2;
                            break;
                        case OperandType.DerefBytePgReg:
                        case OperandType.DerefWordPgReg:
                            data = 0;
                            width = 0;
                            break;
                        case OperandType.DerefPgUImm16:
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
                            data = 0;
                            width = 0; // Already encoded
                            break;
                        case OperandType.Imm8:
                            data = (ushort)operandB.Data1;
                            width = 1;
                            break;
                        case OperandType.Imm16:
                            data = (ushort)operandB.Data1;
                            width = 2;
                            break;
                        case OperandType.DerefBytePgRegPlusSImm:
                        case OperandType.DerefWordPgRegPlusSImm:
                            data = (ushort)operandB.Data2;
                            width = (operandB.Data1 & 0x0C) == 0x04 ? 1 : 2;
                            break;
                        case OperandType.DerefBytePgReg:
                        case OperandType.DerefWordPgReg:
                            data = 0;
                            width = 0;
                            break;
                        case OperandType.DerefPgUImm16:
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

                if (operandC != null)
                {
                    resolveFixupC?.Invoke(stream.Position);

                    ushort data;
                    int width;

                    switch (operandC.Type)
                    {
                        case OperandType.Reg:
                            data = 0;
                            width = 0; // Already encoded
                            break;
                        case OperandType.Imm8:
                            data = (ushort)operandC.Data1;
                            width = 1;
                            break;
                        case OperandType.Imm16:
                            data = (ushort)operandC.Data1;
                            width = 2;
                            break;
                        case OperandType.DerefBytePgRegPlusSImm:
                        case OperandType.DerefWordPgRegPlusSImm:
                            data = (ushort)operandC.Data2;
                            width = (operandC.Data1 & 0x0C) == 0x04 ? 1 : 2;
                            break;
                        case OperandType.DerefBytePgReg:
                        case OperandType.DerefWordPgReg:
                            data = 0;
                            width = 0;
                            break;
                        case OperandType.DerefPgUImm16:
                            writer.Write((byte)operandC.Data1);
                            data = (ushort)operandC.Data2;
                            width = 2;
                            break;
                        default:
                            throw new AssemblyException(i, $"Unhandled operandC type {operandC.Type}");
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

                // [PG:REG] / [PG:REG+sIMM]
                ParsePgRegIndex(regOperand, out data1, out var data1ImmWidth);
                if (data1 != -1)
                {
                    //if (data1ImmWidth != (isByte ? 1 : 2))
                    //    throw new AssemblyException(line,  $"Illegal Addressing Mode for SEG:REG '{operand}'");

                    OperandType operandType;

                    if ((signIdx = indOperand.IndexOfAny(SignChars)) != -1)
                    {
                        // [PG:REG+sIMM]
                        var sign = indOperand[signIdx] == '-' ? -1 : 1;
                        var numberString = indOperand.Substring(signIdx + 1, indOperand.Length - signIdx - 1);
                        if (!TryParseImm(numberString, operandIdx, out var data2ImmWidth, out data2))
                            throw new AssemblyException(line, $"Illegal operand '{operand}'");
                        if (data2ImmWidth > data1ImmWidth)
                            throw new AssemblyException(line, $"Operand sImm would overflow {data2ImmWidth} > {data1ImmWidth}");
                        
                        operandType = isByte ? OperandType.DerefBytePgRegPlusSImm : OperandType.DerefWordPgRegPlusSImm;
                        data2 = (short)(data2 * -sign); // Negative sign because we SUB for sign purposes
                    }
                    else
                    {
                        // [PG:REG]
                        operandType = isByte ? OperandType.DerefBytePgReg : OperandType.DerefWordPgReg;
                        data2 = 0;
                    }

                    return new Operand(operandType, data1, data2);
                }

                // [PG:uIMM16]
                int colonIdx = operand.IndexOf(':');
                var pagOperand = operand[1..colonIdx];
                var immOperand = operand[(colonIdx + 1)..^1];
                data1 = ParsePageIndex(pagOperand, isByte);

                if (data1 == -1)
                    throw new AssemblyException(line, $"Illegal Addressing Mode for PG:REG '{operand}'");

                if (TryParseImm(immOperand, operandIdx, out _, out data2))
                    return new Operand(OperandType.DerefPgUImm16, data1, data2);
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
            "DG:R0", "DG:R1", "DG:R2", "DG:R3", "DG:R4", "DG:R5", "CG:R6", "TG:R7",
            "SG:SP", "SG:R1", "0XE000:R5", "0XC000:R5", "0X8000:R6", "0X4000:R6", "0X2000:R7", "0X0000:R7"
        };
        private static void ParsePgRegIndex(string registerName, out short idx, out int width)
        {
            idx = (short)Array.IndexOf(PgRegs, registerName);
            width = SelectOperandWidth(idx);
        }

        private readonly static string[] ByteAddressingPages =
        {
            "??", "??", "??", "??", "DG", "DG", "CG", "TG",
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

        private static int SelectOperandWidth(int index) => (index & 0xC) == 0x4 ? 1 : 2;

        private readonly static char[] SignChars = { '+', '-' };
        private bool TryParseImm(string value, int operandIdx, out int resultWidth, out short result)
        {
            int sign = value[0] == '-' ? -1 : 1;
            if (value[0] is '+' or '-')
                value = value[1..];

            if (value.All(char.IsDigit))
            {
                var uResult = ushort.Parse(value);
                result = (short)(uResult * sign);
                resultWidth = (result & 0xFF00) == 0 ? 1 : 2;
                return true;
            }

            if (value.Length >= 3 && value[0] == '0' && value[1] is 'x' or 'X'
            &&  value.Skip(2).All(IsHexDigit))
            {
                var uResult = (ushort)Convert.ToInt16(value[2..], 16);
                result = (short)(uResult * sign);
                resultWidth = (result & 0xFF00) == 0 ? 1 : 2;
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
                else if (operandIdx == 2)
                    resolveFixup3 = Fixup;
                else if (operandIdx != -1)
                    throw new ArgumentOutOfRangeException(nameof(operandIdx), operandIdx, "Must be 0, 1, or 2");
                
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

        private static bool IsRegRefOperand(Operand? operand)
        {
            return operand != null && operand.Type
                is OperandType.Reg
                or OperandType.DerefBytePgRegPlusSImm or OperandType.DerefWordPgRegPlusSImm
                or OperandType.DerefBytePgReg or OperandType.DerefWordPgReg
                or OperandType.DerefPgUImm16;
        }

        private sealed record Operand(OperandType Type, short Data1 = 0, short Data2 = 0);

        private enum OutputFormat
        {
            Bin,
            Exe
        }
    }
}
