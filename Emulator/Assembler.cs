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
                    var labelName = line[..colonIdx].ToUpperInvariant();
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
                    TryParseImm16(operandString, -1, out var sOrigin);
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
                                ch = operandString[++j] switch
                                {
                                    '0' => '\0',
                                    'b' => '\b',
                                    'r' => '\r',
                                    'n' => '\n',
                                    't' => '\t',
                                    '^' => '^',
                                    _ => throw new Exception($"Illegal escape code '{operandString[j]}'")
                                };
                            }

                            writer.Write((byte)ch);
                        }
                    }
                    else
                    {
                        operandString = operandString[j..];
                        TryParseImm16(operandString, -1, out var result);
                        writer.Write((byte)(result & 0xFF));
                    }
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
                var operand1 = ParseOperand(j, 0, operand1String);
                var operand2 = ParseOperand(j, 1, operand2String);
                
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
                    if (operandA.Type is OperandType.Reg or OperandType.DerefWordRegPlusImm16
                                      or OperandType.DerefByteRegPlusImm16)
                        opcode |= (ushort)(operandA.Data1 & 0x000F);
                }

                writer.Write((byte)(opcode >> 8));
                if (operandCount > 0)
                    writer.Write((byte)(opcode & 0xFF));

                if (operandA != null)
                {
                    resolveFixupA?.Invoke(stream.Position);
                    switch (operandA.Type)
                    {
                        case OperandType.Imm16:
                        case OperandType.DerefWordImm16:
                        case OperandType.DerefByteImm16:
                        {
                            var imm16 = (ushort)operandA.Data1;
                            writer.Write((byte)(imm16 >> 8));
                            writer.Write((byte)(imm16 & 0xFF));
                            break;
                        }
                        case OperandType.DerefWordRegPlusImm16:
                        case OperandType.DerefByteRegPlusImm16:
                        {
                            var imm16 = (ushort)operandA.Data2;
                            writer.Write((byte)(imm16 >> 8));
                            writer.Write((byte)(imm16 & 0xFF));
                            break;
                        }
                    }
                }

                if (operandB != null)
                {
                    resolveFixupB?.Invoke(stream.Position);
                    switch (operandB.Type)
                    {
                        case OperandType.Reg:
                            writer.Write((byte)operandB.Data1);
                            break;
                        case OperandType.Imm16:
                        case OperandType.DerefWordImm16:
                        case OperandType.DerefByteImm16:
                        {
                            var imm16 = (ushort)operandB.Data1;
                            writer.Write((byte)(imm16 >> 8));
                            writer.Write((byte)(imm16 & 0xFF));
                            break;
                        }
                        case OperandType.DerefWordRegPlusImm16:
                        case OperandType.DerefByteRegPlusImm16:
                        {
                            writer.Write((byte)operandB.Data1);
                            var imm16 = (ushort)operandB.Data2;
                            writer.Write((byte)(imm16 >> 8));
                            writer.Write((byte)(imm16 & 0xFF));
                            break;
                        }
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
            var operandCombination = (byte)(((byte)operand1 << 4) | (byte)operand2);
            var operandCombinationIdx = metadata.OperandCombinations.FindIndex(x => (x & 0x7F) == operandCombination);
            if (operandCombinationIdx == -1)
                throw new AssemblyException(line, $"Opcode '{metadata.Name}' does not support the operand combination {operand1}, {operand2}");

            int result = 0;

            result |= (metadata.Index & 0x1F) << 10;
            result |= ((byte)operand1 & 0x07) << 7;
            result |= ((byte)operand2 & 0x07) << 4;

            if (operandCount == 1)
                result |= 0x8000;
            else if (operandCount == 0)
                result &= 0xFC00;
            
            return ((ushort)result, (metadata.OperandCombinations[operandCombinationIdx] & 0x80) != 0);
        }

        private Operand ParseOperand(int line, int operandIdx, string? operand)
        {
            short data1;
            
            if (string.IsNullOrEmpty(operand))
                return new Operand(OperandType.None);

            // CHARACTERS
            if (operand.Length >= 2 && operand[0] == '\'')
            {
                // TODO Escape codes '^n
                return new Operand(OperandType.Imm16, (short)operand[1]);
            }

            // REG
            if ((data1 = ParseRegisterIndex(operand)) != -1)
                return new Operand(OperandType.Reg, data1);
            
            // IMM16
            if (TryParseImm16(operand, operandIdx, out data1))
                return new Operand(OperandType.Imm16, data1);

            // [REG+IMM16] / [IMM16]
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

                // [REG+IMM16]
                if ((data1 = ParseRegisterIndex(regOperand)) != -1)
                {
                    short data2;
                    if ((signIdx = indOperand.IndexOfAny(SignChars)) != -1)
                    {
                        var sign = indOperand[signIdx] == '-' ? -1 : 1;
                        var numberString = indOperand.Substring(signIdx + 1, indOperand.Length - signIdx - 1);
                        if (!TryParseImm16(numberString, operandIdx, out data2))
                            throw new AssemblyException(line, $"Illegal operand '{operand}'");

                        data2 = (short)(data2 * -sign); // Negative sign because we SUB for sign purposes
                    }
                    else
                        data2 = 0;

                    var operandType = isByte ? OperandType.DerefByteRegPlusImm16 : OperandType.DerefWordRegPlusImm16;
                    return new Operand(operandType, data1, data2);
                }

                // [IMM16]
                if (TryParseImm16(indOperand, operandIdx, out data1))
                {
                    var operandType = isByte ? OperandType.DerefByteImm16 : OperandType.DerefWordImm16;
                    return new Operand(operandType, data1);
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

        private readonly static char[] SignChars = { '+', '-' };
        private bool TryParseImm16(string value, int operandIdx, out short result)
        {
            int sign = value[0] == '-' ? -1 : 1;
            if (value[0] is '+' or '-')
                value = value[1..];

            if (value.All(char.IsDigit))
            {
                result = (short)(short.Parse(value) * sign);
                return true;
            }

            if (value.Length >= 3 && value[0] == '0' && value[1] == 'X'
                &&  value.Skip(2).All(IsHexDigit))
            {
                result = (short)(Convert.ToInt16(value[2..], 16) * sign);
                return true;
            }

            if ((char.IsLetter(value[0]) || value[0] == '_')
            &&  !value.StartsWith("WORD") && !value.StartsWith("BYTE"))
            {
                if (sign == -1) throw new AssemblyException(-1, $"Label '{value}' cannot be negatively addressed");

                if (labels.TryGetValue(value, out result))
                {
                    result += (short)origin;
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
                return true;
            }

            result = 0;
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
