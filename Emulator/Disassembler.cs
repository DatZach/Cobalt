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

            int opcodeIndex = (iword & 0x8000) switch
            {
                0x8000 => (iword & 0xFC00) >> 10,
                0x0000 => (iword & 0xF000) >> 10,
                _ => -1
            };

            if (!opcodeMetadata.TryGetValue(opcodeIndex, out var metadata))
                return $"; UNK {iword:X4}";

            string? operandA = null;
            string? operandB = null;
            string? operandC = null;
            var operand1Type = metadata.OperandCount >= 1 ? (OperandType)((iword & 0x0038) >> 3) : OperandType.None;
            var operand2Type = metadata.OperandCount >= 2 ? (OperandType)(iword & 0x0007) : OperandType.None;
            OperandType operand3Type = OperandType.None;
            var isFlipped = false;

            if (metadata.OperandCount >= 2)
            {
                var operandCombo = metadata.OperandCombinations.FirstOrDefault(x => x.A == operand1Type && x.B == operand2Type);
                if (operandCombo == null)
                    return $"; UNK {iword:X4}";

                isFlipped = operandCombo.RTL;
            }

            if (isFlipped)
                (operand1Type, operand2Type) = (operand2Type, operand1Type);

            if (metadata.OperandCount == 3)
            {
                operand3Type = operand2Type;
                operand2Type = operand1Type;
            }

            ushort regWord;
            if (IsRegRefOperand(operand1Type) || IsRegRefOperand(operand2Type) || IsRegRefOperand(operand3Type))
            {
                if (IsRegRefOperand(operand3Type))
                {
                    regWord = machine.ReadWord(segment, offset);
                    offset += 2;
                }
                else
                {
                    regWord = (ushort)(machine.ReadByte(segment, offset) << 8);
                    offset += 1;
                }
            }
            else
                regWord = 0;

            switch (operand1Type)
            {
                case OperandType.Reg:
                    operandA = ParseRegisterName((regWord & 0xF000) >> 12);
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

                case OperandType.DerefWordPgReg:
                case OperandType.DerefBytePgReg:
                {
                    var operand = (regWord & 0xF000) >> 12;
                    var busWidthName = operand1Type == OperandType.DerefWordPgReg ? "WORD" : "BYTE";
                    var regName = ParsePgRegIndex(operand);
                    operandA = $"{busWidthName} [{regName}]";
                    break;
                }

                case OperandType.DerefWordPgRegPlusSImm:
                case OperandType.DerefBytePgRegPlusSImm:
                {
                    var operand = (regWord & 0xF000) >> 12;
                    var busWidthName = operand1Type == OperandType.DerefWordPgRegPlusSImm ? "WORD" : "BYTE";
                    var regName = ParsePgRegIndex(operand);
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

                case OperandType.DerefPgUImm16:
                {
                    var operand = (regWord & 0xF000) >> 12;
                    var segName = ParsePageIndex(operand);
                    var immWidth = SelectOperandWidth(operand);
                    var busWidthName = immWidth == 2 ? "WORD" : "BYTE";
                    operandA = $"{busWidthName} [{segName}:0x{machine.ReadWord(segment, offset):X4}]";
                    offset += 2;
                    break;
                }
            }

            switch (operand2Type)
            {
                case OperandType.Reg:
                    operandB = ParseRegisterName((regWord & 0x0F00) >> 8);
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

                case OperandType.DerefWordPgReg:
                case OperandType.DerefBytePgReg:
                {
                    var operand = (regWord & 0x0F00) >> 8;
                    var busWidthName = operand2Type == OperandType.DerefWordPgReg ? "WORD" : "BYTE";
                    var regName = ParsePgRegIndex(operand);
                    operandB = $"{busWidthName} [{regName}]";
                    offset += 1;
                    break;
                }

                case OperandType.DerefWordPgRegPlusSImm:
                case OperandType.DerefBytePgRegPlusSImm:
                {
                    var operand = (regWord & 0x0F00) >> 8;
                    var busWidthName = operand2Type == OperandType.DerefWordPgRegPlusSImm ? "WORD" : "BYTE";
                    var regName = ParsePgRegIndex(operand);
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

                case OperandType.DerefPgUImm16:
                {
                    var operand = (regWord & 0x0F00) >> 8;
                    var segName = ParsePageIndex(operand);
                    var immWidth = SelectOperandWidth(operand);
                    var busWidthName = immWidth == 2 ? "WORD" : "BYTE";
                    offset += 1;
                    operandB = $"{busWidthName} [{segName}:0x{machine.ReadWord(segment, offset):X4}]";
                    offset += 2;
                    break;
                }
            }

            switch (operand3Type)
            {
                case OperandType.Reg:
                    operandC = ParseRegisterName((regWord & 0x00F0) >> 4);
                    break;

                case OperandType.Imm8:
                {
                    operandC = $"0x{machine.ReadByte(segment, offset):X2}";
                    offset += 1;
                    break;
                }

                case OperandType.Imm16:
                {
                    operandC = $"0x{machine.ReadWord(segment, offset):X4}";
                    offset += 2;
                    break;
                }

                case OperandType.DerefWordPgReg:
                case OperandType.DerefBytePgReg:
                {
                    var operand = (regWord & 0x00F0) >> 4;
                    var busWidthName = operand2Type == OperandType.DerefWordPgReg ? "WORD" : "BYTE";
                    var regName = ParsePgRegIndex(operand);
                    operandC = $"{busWidthName} [{regName}]";
                    offset += 1;
                    break;
                }

                case OperandType.DerefWordPgRegPlusSImm:
                case OperandType.DerefBytePgRegPlusSImm:
                {
                    var operand = (regWord & 0x00F0) >> 4;
                    var busWidthName = operand2Type == OperandType.DerefWordPgRegPlusSImm ? "WORD" : "BYTE";
                    var regName = ParsePgRegIndex(operand);
                    var immWidth = SelectOperandWidth(operand);
                    offset += 1;
                    short immValue;
                    if (immWidth == 1)
                        immValue = (short)-machine.ReadByte(segment, offset);
                    else
                        immValue = (short)-machine.ReadWord(segment, offset);
                    offset += (ushort)immWidth;
                    if (immValue == 0)
                        operandC = $"{busWidthName} [{regName}]";
                    else
                    {
                        var signStr = (immValue & 0x8000) != 0 ? "" : "+";
                        operandC = $"{busWidthName} [{regName}{signStr}{immValue}]";
                    }
                    break;
                }

                case OperandType.DerefPgUImm16:
                {
                    var operand = (regWord & 0x00F0) >> 4;
                    var segName = ParsePageIndex(operand);
                    var immWidth = SelectOperandWidth(operand);
                    var busWidthName = immWidth == 2 ? "WORD" : "BYTE";
                    offset += 1;
                    operandC = $"{busWidthName} [{segName}:0x{machine.ReadWord(segment, offset):X4}]";
                    offset += 2;
                    break;
                }
            }

            if (isFlipped)
            {
                if (metadata.OperandCount == 2)
                    (operandA, operandB) = (operandB, operandA);
                else if (metadata.OperandCount == 3)
                    (operandA, operandB, operandC) = (operandC, operandB, operandA);
            }

            return metadata.OperandCount switch
            {
                0 => metadata.Name,
                1 => $"{metadata.Name} {operandA}",
                2 => $"{metadata.Name} {operandA}, {operandB}",
                3 => $"{metadata.Name} {operandA}, {operandB}, {operandC}",
                _ => $"; UNK {iword:X4}"
            };
        }

        private readonly static string[] Registers =
        {
            "R0", "R1", "R2", "R3", "R4", "R5", "R6", "R7",
            "SP", "SG", "CG", "DG", "TG", "R0L", "R0H", "R1L"
        };
        private static string ParseRegisterName(int idx)
        {
            return Registers[idx];
        }

        private readonly static string[] PgRegs =
        {
            "DG:R0", "DG:R1", "DG:R2", "DG:R3", "DG:R4", "DG:R5", "CG:R6", "TG:R7",
            "SG:SP", "SG:R1", "0xE000:R5", "0xC000:R5", "0x8000:R6", "0x4000:R6", "0x2000:R7", "0x0000:R7"
        };
        private static string ParsePgRegIndex(int idx)
        {
            return PgRegs[idx];
        }

        private readonly static string[] Pages =
        {
            "DG", "DG", "DG", "DG", "DG", "DG", "CG", "TG",
            "SG", "SG", "0xE000", "0xC000", "0x8000", "0x4000", "0x2000", "0x0000"
        };
        private static string ParsePageIndex(int idx)
        {
            return Pages[idx];
        }

        private static int SelectOperandWidth(int index) => (index & 0xC) == 0x4 ? 1 : 2;

        private static bool IsRegRefOperand(OperandType operandType)
        {
            return operandType
                is OperandType.Reg
                or OperandType.DerefBytePgRegPlusSImm or OperandType.DerefWordPgRegPlusSImm
                or OperandType.DerefBytePgReg or OperandType.DerefWordPgReg
                or OperandType.DerefPgUImm16;
        }
    }
}
