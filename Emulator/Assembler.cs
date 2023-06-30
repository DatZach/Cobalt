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
                var opcodeString = string.Join("", line.TakeWhile(char.IsLetter));
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

                var operand1 = ParseOperandType(j, operand1String);
                var operand2 = ParseOperandType(j, operand2String);

                var opcode = ParseOpcode(j, opcodeString, operandCount, operand1, operand2);
                if (operandCount == 0)
                {
                    writer.Write((byte)(opcode >> 8));
                    continue;
                }

                if (operandCount >= 1)
                {
                    if (operand1 == Operand.Reg)
                        opcode |= (ushort)(ParseRegisterIndex(operand1String) & 0x000F);

                    writer.Write((byte)(opcode >> 8));
                    writer.Write((byte)(opcode & 0xFF));
                    switch (operand1)
                    {
                        case Operand.Imm16:
                            var imm16 = (ushort)int.Parse(operand1String);
                            writer.Write((byte)(imm16 >> 8));
                            writer.Write((byte)(imm16 & 0xFF));
                            break;
                        case Operand.DerefRegPlusImm16:
                            throw new NotImplementedException();
                        case Operand.DerefImm16:
                            throw new NotImplementedException();
                    }
                }

                if (operandCount >= 2)
                {
                    switch (operand2)
                    {
                        case Operand.Reg:
                            var reg2 = (byte)(ParseRegisterIndex(operand2String) & 0x000F);
                            writer.Write(reg2);
                            break;
                        case Operand.Imm16:
                            var imm16 = (ushort)int.Parse(operand2String);
                            writer.Write((byte)(imm16 >> 8));
                            writer.Write((byte)(imm16 & 0xFF));
                            break;
                        case Operand.DerefRegPlusImm16:
                            throw new NotImplementedException();
                        case Operand.DerefImm16:
                            throw new NotImplementedException();
                    }
                }
            }

            return stream.ToArray();
        }

        private ushort ParseOpcode(int line, string opcode, int operandCount, Operand operand1, Operand operand2)
        {
            opcode = opcode.ToUpperInvariant();
            if (!opcodeMetadata.TryGetValue(opcode, out var metadata))
                throw new AssemblyException(line, $"Unknonw opcode '{opcode}'");
            if (metadata.OperandCount != operandCount)
                throw new AssemblyException(line, $"Opcode expected {metadata.OperandCount} operands, received {operandCount} instead");
            var operandCombination = (byte)(((byte)operand1 << 4) | (byte)operand2);
            if (!metadata.OperandCombinations.Contains(operandCombination))
                throw new AssemblyException(line, $"Opcode does not support the operand combination {operand1}, {operand2}");

            int result = 0;

            result |= (metadata.Index & 0x1F) << 10;
            result |= ((byte)operand1 & 0x07) << 7;
            result |= ((byte)operand2 & 0x07) << 4;

            if (operandCount == 1)
                result |= 0x8000;
            else if (operandCount == 0)
                result &= 0xFC00;
            
            return (ushort)result;
        }

        private Operand ParseOperandType(int line, string? operand)
        {
            if (string.IsNullOrEmpty(operand))
                return Operand.None;
            if (ParseRegisterIndex(operand) != -1)
                return Operand.Reg;
            if (operand.All(char.IsDigit))
                return Operand.Imm16;

            throw new AssemblyException(line, $"Illegal operand '{operand}'");
        }

        private readonly static string[] Registers =
        {
            "R0", "R1", "R2", "R3", "SP", "SS", "CS", "DS",
            "R0H", "R0L", "R1H", "R1L", "R2H", "R2L", "R3H", "R3L"
        };
        private int ParseRegisterIndex(string registerName)
        {
            return Array.IndexOf(Registers, registerName);
        }
    }
}
