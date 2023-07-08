namespace Emulator
{
    public sealed class Assembler
    {
        private readonly Dictionary<string, MicrocodeRom.Opcode> opcodeMetadata;

        public Assembler(MicrocodeRom microcodeRom)
        {
            opcodeMetadata = microcodeRom?.OpcodeMetadata ?? throw new ArgumentNullException(nameof(microcodeRom));
        }

        public byte[] AssembleSource(string source)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            var lines = source.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                var semiIdx = line.IndexOf(';');
                if (semiIdx >= 0)
                    line = line[..semiIdx];

                line = line.Trim();

                if (string.IsNullOrEmpty(line))
                    continue;

                string? operand1String = null;
                string? operand2String = null;
                var opcodeString = string.Join("", line.TakeWhile(char.IsLetter)).ToUpperInvariant();
                int j = opcodeString.Length;
                while (j < line.Length && char.IsWhiteSpace(line[j]))
                    ++j;

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

                var operand1 = ParseOperand(j, operand1String);
                var operand2 = ParseOperand(j, operand2String);
                
                if (!opcodeMetadata.TryGetValue(opcodeString, out var metadata))
                    throw new AssemblyException(j, $"Unknonw opcode '{opcodeString}'");

                var (opcode, operandOrder) = ParseOpcode(j, metadata, operandCount, operand1.Type, operand2.Type);

                Operand? operandB, operandA;
                if (operandCount == 2)
                {
                    if (!operandOrder)
                    {
                        operandA = operand1;
                        operandB = operand2;
                    }
                    else if (operandOrder)
                    {
                        operandA = operand2;
                        operandB = operand1;
                    }
                    else
                        throw new AssemblyException(j, $"Illegal operand direction specified in metadata '{opcodeString}'");
                }
                else if (operandCount == 1)
                {
                    operandA = operand1;
                    operandB = null;
                }
                else
                {
                    operandA = null;
                    operandB = null;
                }

                if (operandA != null)
                {
                    if (operandA.Type == OperandType.Reg || operandA.Type == OperandType.DerefRegPlusImm16)
                        opcode |= (ushort)(operandA.Data1 & 0x000F);
                }

                writer.Write((byte)(opcode >> 8));
                if (operandCount > 0)
                    writer.Write((byte)(opcode & 0xFF));

                if (operandA != null)
                {
                    switch (operandA.Type)
                    {
                        case OperandType.Imm16:
                        case OperandType.DerefImm16:
                        {
                            var imm16 = (ushort)operandA.Data1;
                            writer.Write((byte)(imm16 >> 8));
                            writer.Write((byte)(imm16 & 0xFF));
                            break;
                        }
                        case OperandType.DerefRegPlusImm16:
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
                    switch (operandB.Type)
                    {
                        case OperandType.Reg:
                            writer.Write((byte)operandB.Data1);
                            break;
                        case OperandType.Imm16:
                        case OperandType.DerefImm16:
                        {
                            var imm16 = (ushort)operandB.Data1;
                            writer.Write((byte)(imm16 >> 8));
                            writer.Write((byte)(imm16 & 0xFF));
                            break;
                        }
                        case OperandType.DerefRegPlusImm16:
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

            return stream.ToArray();
        }

        private (ushort, bool) ParseOpcode(int line, MicrocodeRom.Opcode metadata, int operandCount, OperandType operand1, OperandType operand2)
        {
            if (metadata.OperandCount != operandCount)
                throw new AssemblyException(line, $"Opcode expected {metadata.OperandCount} operands, received {operandCount} instead");
            var operandCombination = (byte)(((byte)operand1 << 4) | (byte)operand2);
            var operandCombinationIdx = metadata.OperandCombinations.FindIndex(x => (x & 0x7F) == operandCombination);
            if (operandCombinationIdx == -1)
                throw new AssemblyException(line, $"Opcode does not support the operand combination {operand1}, {operand2}");

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

        private Operand ParseOperand(int line, string? operand)
        {
            short data1;
            
            if (string.IsNullOrEmpty(operand))
                return new Operand(OperandType.None);

            // REG
            if ((data1 = ParseRegisterIndex(operand)) != -1)
                return new Operand(OperandType.Reg, data1);
            
            // IMM16
            if (TryParseNumber(operand, out data1))
                return new Operand(OperandType.Imm16, data1);

            // [REG+IMM16] / [IMM16]
            if (operand[0] == '[' && operand[^1] == ']')
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
                        if (!TryParseNumber(numberString, out data2))
                            throw new AssemblyException(line, $"Illegal operand '{operand}'");

                        data2 = (short)(data2 * -sign); // Negative sign because we SUB for sign purposes
                    }
                    else
                        data2 = 0;

                    return new Operand(OperandType.DerefRegPlusImm16, data1, data2);
                }

                // [IMM16]
                if (TryParseNumber(indOperand, out data1))
                    return new Operand(OperandType.DerefImm16, data1);
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
        private static bool TryParseNumber(string value, out short result)
        {
            if (value.All(char.IsDigit))
            {
                result = short.Parse(value);
                return true;
            }

            if (value.Length >= 3 && value[0] == '0' && value[1] == 'X'
                &&  value.Skip(2).All(IsHexDigit))
            {
                result = Convert.ToInt16(value[2..], 16);
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
    }
}
