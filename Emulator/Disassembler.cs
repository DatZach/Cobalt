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

            var conditional = (iword & 0x8000) switch
            {
                0x8000 => (Conditional)((iword & 0x03C0) >> 6),
                0x0000 => (Conditional)((iword & 0x0F00) >> 8),
                _ => Conditional.None
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

            for (int k = 0; k < metadata.OperandCount; ++k)
            {
                OperandType type;
                if (k == 0)
                    type = operand1Type;
                else if (k == 1)
                    type = operand2Type;
                else if (k == 2)
                    type = operand3Type;
                else
                    return $"; UNK {iword:X4}";

                var ko = 12 - k * 4;
                var regOperand = (regWord & (0xF << ko)) >> ko;

                string value;
                switch (type)
                {
                    case OperandType.Reg:
                        value = ParseRegisterName(regOperand);
                        break;

                    case OperandType.Imm:
                    {
                        value = $"0x{machine.ReadWord(segment, offset):X4}";
                        offset += 2;
                        break;
                    }

                    case OperandType.DerefWordPgReg:
                    case OperandType.DerefBytePgReg:
                    {
                        var busWidthName = operand1Type == OperandType.DerefWordPgReg ? "WORD" : "BYTE";
                        var regName = ParsePgRegIndex(regOperand);
                        value = $"{busWidthName} [{regName}]";
                        break;
                    }

                    case OperandType.DerefWordPgRegPlusSImm:
                    case OperandType.DerefBytePgRegPlusSImm:
                    {
                        var busWidthName = operand1Type == OperandType.DerefWordPgRegPlusSImm ? "WORD" : "BYTE";
                        var regName = ParsePgRegIndex(regOperand);
                        var immWidth = 2;//SelectOperandWidth(regOperand);

                        short immValue;
                        if (immWidth == 1)
                            immValue = (short)-machine.ReadByte(segment, offset);
                        else
                            immValue = (short)-machine.ReadWord(segment, offset);
                        offset += (ushort)immWidth;

                        if (immValue == 0)
                            value = $"{busWidthName} [{regName}]";
                        else
                        {
                            var signStr = (immValue & 0x8000) != 0 ? "" : "+";
                            value = $"{busWidthName} [{regName}{signStr}{immValue}]";
                        }
                        break;
                    }

                    case OperandType.DerefBytePgUImm:
                    {
                        var segName = ParsePageIndex(regOperand);
                        value = $"BYTE [{segName}:0x{machine.ReadWord(segment, offset):X4}]";
                        offset += 2;
                        break;
                    }

                    case OperandType.DerefWordPgUImm:
                    {
                        var segName = ParsePageIndex(regOperand);
                        value = $"WORD [{segName}:0x{machine.ReadWord(segment, offset):X4}]";
                        offset += 2;
                        break;
                    }

                    default:
                        value = "";
                        break;
                }

                if (k == 0)
                    operandA = value;
                else if (k == 1)
                    operandB = value;
                else if (k == 2)
                    operandC = value;
            }
            
            if (isFlipped)
            {
                if (metadata.OperandCount == 2)
                    (operandA, operandB) = (operandB, operandA);
                else if (metadata.OperandCount == 3)
                    (operandA, operandB, operandC) = (operandC, operandB, operandA);
            }

            var conditionalName = conditional != Conditional.None ? $".{conditional.ToString().ToUpperInvariant()}" : "";

            return metadata.OperandCount switch
            {
                0 => $"{metadata.Name}{conditionalName}",
                1 => $"{metadata.Name}{conditionalName} {operandA}",
                2 => $"{metadata.Name}{conditionalName} {operandA}, {operandB}",
                3 => $"{metadata.Name}{conditionalName} {operandA}, {operandB}, {operandC}",
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
            "DG:R0", "DG:R1", "DG:R2", "DG:R3", "DG:R4", "SG:R5", "CG:R6", "TG:R7",
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
                or OperandType.DerefBytePgUImm;
        }
    }
}
