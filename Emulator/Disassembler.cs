namespace Emulator
{
    public sealed class Disassembler
    {
        private readonly Dictionary<int, MicrocodeRom.Opcode> opcodeMetadata;
        private readonly Machine machine;

        public Disassembler(MicrocodeRom microcodeRom, Machine machine)
        {
            opcodeMetadata = microcodeRom?.OpcodeMetadata?.ToDictionary(
                x => x.Value.Index,
                x => x.Value
            ) ?? throw new ArgumentNullException(nameof(microcodeRom));

            this.machine = machine ?? throw new ArgumentNullException(nameof(machine));
        }

        public string Disassemble(ushort segment, ushort offset)
        {
            // 0 Operand (0OOOOO00 XXXXXXXX) NO FLAGS
            // 1 Operand (1OOOOOAA AXXXXXXX) + Flags
            // 2 Operand (0OOOOOAA ABBBXXXX) NO FLAGS

            var iword = machine.ReadWord(segment, offset);
            offset += 2;

            int opcodeIndex = (iword & 0xFC00) >> 10;
            if (!opcodeMetadata.TryGetValue(opcodeIndex, out var metadata))
                return $"; UNK {iword:X4}";

            int operandCount = (opcodeIndex & 0x30) switch
            {
                0x30 => 1,
                0x20 => 1,
                0x10 => 2,
                0x00 => 0,
                _ => 0
            };

            string? operandA = null;
            string? operandB = null;
            var operand1Type = OperandType.None;
            var operand2Type = OperandType.None;

            if (operandCount >= 1)
            {
                operand1Type = (OperandType)((iword & 0x0380) >> 7);
                switch (operand1Type)
                {
                    case OperandType.Reg:
                        operandA = ParseRegisterName(iword & 0x000F);
                        break;

                    case OperandType.Imm8:
                    {
                        operandA = $"0x{machine.ReadByte(segment, offset):X2}";
                        offset += 1;
                        break;
                    }

                    case OperandType.Imm16:
                    {
                        operandA = $"0x{machine.ReadWord(segment, offset):X4}";
                        offset += 2;
                        break;
                    }

                    case OperandType.DerefWordSegReg:
                    case OperandType.DerefByteSegReg:
                    {
                        var operand = iword & 0x000F;
                        var busWidthName = operand1Type == OperandType.DerefWordSegReg ? "WORD" : "BYTE";
                        var regName = ParseSegRegIndex(operand);
                        operandA = $"{busWidthName} [{regName}]";
                        break;
                    }

                    case OperandType.DerefWordSegRegPlusSImm:
                    case OperandType.DerefByteSegRegPlusSImm:
                    {
                        var operand = iword & 0x000F;
                        var busWidthName = operand1Type == OperandType.DerefWordSegRegPlusSImm ? "WORD" : "BYTE";
                        var regName = ParseSegRegIndex(operand);
                        var immWidth = SelectOperandWidth(operand);

                        short immValue;
                        if (immWidth == 1)
                            immValue = (short)-machine.ReadByte(segment, offset);
                        else
                            immValue = (short)-machine.ReadWord(segment, offset);
                        offset += (ushort)immWidth;

                        if (immValue == 0)
                            operandA = $"{busWidthName} [{regName}]";
                        else
                        {
                            var signStr = (immValue & 0x8000) != 0 ? "" : "+";
                            operandA = $"{busWidthName} [{regName}{signStr}{immValue}]";
                        }
                        break;
                    }

                    case OperandType.DerefSegUImm16:
                    {
                        var operand = iword & 0x000F;
                        var segName = ParseSegmentIndex(operand);
                        var immWidth = SelectOperandWidth(operand);
                        var busWidthName = immWidth == 2 ? "WORD" : "BYTE";
                        operandB = $"{busWidthName} [{segName}:0x{machine.ReadWord(segment, offset):X4}]";
                        offset += 2;
                        break;
                    }
                }
            }

            if (operandCount >= 2)
            {
                operand2Type = (OperandType)((iword & 0x0070) >> 4);
                switch (operand2Type)
                {
                    case OperandType.Reg:
                        operandB = ParseRegisterName(machine.ReadByte(segment, offset) & 0x0F);
                        offset += 1;
                        break;

                    case OperandType.Imm8:
                    {
                        operandB = $"0x{machine.ReadByte(segment, offset):X2}";
                        offset += 1;
                        break;
                    }

                    case OperandType.Imm16:
                    {
                        operandB = $"0x{machine.ReadWord(segment, offset):X4}";
                        offset += 2;
                        break;
                    }

                    case OperandType.DerefWordSegReg:
                    case OperandType.DerefByteSegReg:
                    {
                        var operand = machine.ReadByte(segment, offset) & 0x0F;
                        var busWidthName = operand2Type == OperandType.DerefWordSegReg ? "WORD" : "BYTE";
                        var regName = ParseSegRegIndex(operand);
                        operandB = $"{busWidthName} [{regName}]";
                        offset += 1;
                        break;
                    }

                    case OperandType.DerefWordSegRegPlusSImm:
                    case OperandType.DerefByteSegRegPlusSImm:
                    {
                        var operand = machine.ReadByte(segment, offset) & 0x0F;
                        var busWidthName = operand2Type == OperandType.DerefWordSegRegPlusSImm ? "WORD" : "BYTE";
                        var regName = ParseSegRegIndex(operand);
                        var immWidth = SelectOperandWidth(operand);
                        offset += 1;
                        short immValue;
                        if (immWidth == 1)
                            immValue = (short)-machine.ReadByte(segment, offset);
                        else
                            immValue = (short)-machine.ReadWord(segment, offset);
                        offset += (ushort)immWidth;
                        if (immValue == 0)
                            operandB = $"{busWidthName} [{regName}]";
                        else
                        {
                            var signStr = (immValue & 0x8000) != 0 ? "" : "+";
                            operandB = $"{busWidthName} [{regName}{signStr}{immValue}]";
                        }
                        break;
                    }

                    case OperandType.DerefSegUImm16:
                    {
                        var operand = machine.ReadByte(segment, offset) & 0x0F;
                        var segName = ParseSegmentIndex(operand);
                        var immWidth = SelectOperandWidth(operand);
                        var busWidthName = immWidth == 2 ? "WORD" : "BYTE";
                        operandB = $"{busWidthName} [{segName}:0x{machine.ReadWord(segment, offset):X4}]";
                        offset += 2;
                        break;
                    }
                }
            }

            switch (operandCount)
            {
                case 0:
                    return metadata.Name;

                case 1:
                    return $"{metadata.Name} {operandA}";

                case 2:
                {
                    var operandCombination = (byte)(((byte)operand1Type << 4) | (byte)operand2Type);
                    var operandCombinationIdx = metadata.OperandCombinations.FindIndex(x => (x & 0x7F) == operandCombination);
                    if (operandCombinationIdx != -1
                        && (metadata.OperandCombinations[operandCombinationIdx] & 0x80) != 0)
                    {
                        return $"{metadata.Name} {operandB}, {operandA}";
                    }
                    
                    return $"{metadata.Name} {operandA}, {operandB}";
                }
            }

            return $"; UNK {iword:X4}";
        }

        private readonly static string[] Registers =
        {
            "R0", "R1", "R2", "R3", "SP", "SS", "CS", "DS",
            "R0H", "R0L", "R1H", "R1L", "R2H", "R2L", "R3H", "R3L"
        };
        private static string ParseRegisterName(int idx)
        {
            return Registers[idx];
        }

        private readonly static string[] SegRegs =
        {
            "DS:R0", "DS:R1", "DS:R2", "DS:R3", "SS:SP", "SS:R1", "CS:R2", "DS:R3",
            "SS:R0", "CS:R0", "0XE000:R1", "0XC000:R1", "0X8000:R2", "0X4000:R2", "0X2000:R3", "0X0000:R3"
        };
        private static string ParseSegRegIndex(int idx)
        {
            return SegRegs[idx];
        }

        private readonly static string[] Segments =
        {
            "DS:", "DS:", "DS:", "DS:", "SS:", "SS:", "CS:", "DS:",
            "SS:", "CS:", "0XE000:", "0XC000:", "0X8000:", "0X4000:", "0X2000:", "0X0000:"
        };
        private static string ParseSegmentIndex(int idx)
        {
            return Segments[idx];
        }

        private static int SelectOperandWidth(int index) => (index & 0xC) == 0x4 ? 1 : 2;
    }
}
