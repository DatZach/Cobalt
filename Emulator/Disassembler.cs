using System.Text;

namespace Emulator
{
    public sealed class Disassembler
    {
        private readonly Dictionary<int, MicrocodeRom.Opcode> opcodeMetadata;
        private readonly Dictionary<int, IReadOnlyList<OperandType>> reverseOperandTable;
        private readonly Machine machine;

        public Disassembler(MicrocodeRom microcodeRom, Machine machine)
        {
            opcodeMetadata = microcodeRom?.OpcodeMetadata?.ToDictionary(
                x => x.Value.Index | (x.Value.OperandCount == 0 ? 0x8000 : 0),
                x => x.Value
            ) ?? throw new ArgumentNullException(nameof(microcodeRom));

            reverseOperandTable = new Dictionary<int, IReadOnlyList<OperandType>>();
            foreach (var kvp in Microcode.OperandTable)
            {
                foreach (var value in kvp.Value)
                {
                    var parts = kvp.Key.Split(' ');
                    var key = ((parts.Length & 0x07) << 8) | value;

                    reverseOperandTable[key] = parts.Select(x => x switch
                    {
                        "REG" => OperandType.Reg,
                        "IMM" => OperandType.Imm,
                        "SZ[PG:REG+sIMM]" => OperandType.DerefSizePgRegPlusSImm,
                        "SZ[PG:REG]" => OperandType.DerefSizePgReg,
                        "SZ[PG:uIMM]" => OperandType.DerefSizePgUImm,
                        _ => throw new ArgumentOutOfRangeException(nameof(x), x, "Unknown Operand Type Name")
                    }).ToList();
                }
            }

            this.machine = machine ?? throw new ArgumentNullException(nameof(machine));
        }

        public string Disassemble(ushort segment, ushort offset)
        {
            // 0 Operand (0OOOOO00 XXXXXXXX) NO FLAGS
            // 1 Operand (1OOOOOAA AXXXXXXX) + Flags
            // 2 Operand (0OOOOOAA ABBBXXXX) NO FLAGS

            var iword = machine.ReadWord(segment, offset);
            var operand0 = machine.ReadWord(segment, (ushort)(offset + 2));
            var operand1 = machine.ReadWord(segment, (ushort)(offset + 4));
            offset += 2;

            int opcodeIndex;
            if ((iword & 0x8F00) == 0)
                opcodeIndex = ((iword & 0x7000) >> 12) | 0x8000;
            else
                opcodeIndex = ((iword & 0x7000) >> 10) | ((iword & 0x00C0) >> 6);

            var conditional = (iword & 0x8000) switch
            {
                0x0000 => (Conditional)((iword & 0x0F00) >> 8),
                _ => Conditional.None
            };

            if (!opcodeMetadata.TryGetValue(opcodeIndex, out var metadata))
                return $"; UNK {iword:X4} {operand0:X4} {operand1:X4}";

            var opcodeName = metadata.Name;
            if (metadata.Name is "JMPS" or "JMPL") opcodeName = "JMP";

            var sb = new StringBuilder(80);
            sb.Append(opcodeName);
            if (conditional != Conditional.None && conditional != Conditional.Operandless)
            {
                sb.Append('.');
                sb.Append(conditional.ToString().ToUpperInvariant());
            }

            sb.Append(' ');

            var iT = (iword & 0x8000) >> 15;
            var iS = (iword & 0x20) != 0 ? 1 : 0;
            var operandIndex = ((metadata.OperandCount & 0x07) << 8) | (iT << 5) | (iword & 0x1F);

            IReadOnlyList<OperandType>? operands = null;
            if (metadata.OperandCount > 0 && !reverseOperandTable.TryGetValue(operandIndex, out operands))
                return $"; UNK {iword:X4} {operand0:X4} {operand1:X4}";

            var operandFormat = Microcode.OperandFormats[operandIndex & 0x07];
            int regIndex = 0, immIndex = 0;
            for (int k = 0; operands != null && k < metadata.OperandCount; ++k)
            {
                var operand = operands[k];
                int regOperand;
                short immValue;

                if (IsRegRefOperand(operand))
                {
                    var idx = (iT << 4) | regIndex;
                    regOperand = idx switch
                    {
                        0x00 => (operand0 & 0xF000) >> 12,
                        0x01 => (operand0 & 0x0F00) >> 8,
                        0x02 => (operand0 & 0x00F0) >> 4,
                        0x10 => (iword & 0x0F00) >> 8,
                        0x11 => (operand0 & 0xF000) >> 12,
                        0x12 => (operand0 & 0x0F00) >> 8,
                        _ => throw new ArgumentOutOfRangeException(nameof(idx), idx, "Illegal RegIndex")
                    };

                    ++regIndex;
                }
                else
                    regOperand = -1;

                if (IsImmRefOperand(operand))
                {
                    int immOffset, immLength;
                    if (immIndex == 0)
                    {
                        immOffset = operandFormat.OffsetA;
                        immLength = operandFormat.LengthA;
                    }
                    else if (immIndex == 1)
                    {
                        immOffset = operandFormat.OffsetB;
                        immLength = operandFormat.LengthB;
                    }
                    else
                        throw new ArgumentOutOfRangeException(nameof(immIndex), immIndex, "Illegal ImmIndex");

                    var imm = ((long)iword << 32) | ((long)operand0 << 16) | operand1;
                    var lsbOffset = 48 - immOffset - immLength;
                    var immMask = (int)Math.Pow(2, immLength) - 1;
                    immValue = (short)((imm & (immMask << lsbOffset)) >> lsbOffset);

                    ++immIndex;
                }
                else
                    immValue = -1;

                var busWidthName = iS == 1 ? "WORD" : "BYTE";
                string value;
                switch (operand)
                {
                    case OperandType.Reg:
                        value = ParseRegisterName(regOperand);
                        break;

                    case OperandType.Imm:
                        value = $"0x{immValue:X4}";
                        break;

                    case OperandType.DerefSizePgReg:
                    {
                        var regName = ParsePgRegIndex(regOperand);
                        value = $"{busWidthName} [{regName}]";
                        break;
                    }

                    case OperandType.DerefSizePgRegPlusSImm:
                    {
                        var regName = ParsePgRegIndex(regOperand);

                        if (immValue == 0)
                            value = $"{busWidthName} [{regName}]";
                        else
                        {
                            immValue = (short)-immValue;
                            var signStr = (immValue & 0x8000) != 0 ? "" : "+";
                            value = $"{busWidthName} [{regName}{signStr}{immValue}]";
                        }
                        break;
                    }

                    case OperandType.DerefSizePgUImm:
                    {
                        var segName = ParsePageIndex(regOperand);
                        value = $"{busWidthName} [{segName}:0x{machine.ReadWord(segment, offset):X4}]";
                        offset += 2;
                        break;
                    }

                    default:
                        value = "<UNK>";
                        break;
                }

                sb.Append(value);
                if (k < metadata.OperandCount - 1)
                    sb.Append(", ");
            }

            return sb.ToString();
        }

        private readonly static string[] Registers =
        {
            "R0", "R1", "R2", "R3", "R4", "R5", "R6", "R7",
            "SP", "SG", "CG", "DG", "TG", "R0L", "R0H", "R1L"
        };
        private static string ParseRegisterName(int idx)
        {
            return Registers.ElementAtOrDefault(idx) ?? "??";
        }

        private readonly static string[] PgRegs =
        {
            "DG:R0", "DG:R1", "DG:R2", "DG:R3", "DG:R4", "SG:R5", "TG:R6", "CG:R7",
            "SG:SP", "SG:R1", "0xE000:R5", "0xC000:R5", "0x8000:R6", "0x4000:R6", "0x2000:R7", "0x0000:R7"
        };
        private static string ParsePgRegIndex(int idx)
        {
            return PgRegs.ElementAtOrDefault(idx) ?? "??";
        }

        private readonly static string[] Pages =
        {
            "DG", "DG", "DG", "DG", "DG", "DG", "TG", "CG",
            "SG", "SG", "0xE000", "0xC000", "0x8000", "0x4000", "0x2000", "0x0000"
        };
        private static string ParsePageIndex(int idx)
        {
            return Pages.ElementAtOrDefault(idx) ?? "??";
        }

        private static bool IsRegRefOperand(OperandType operandType)
        {
            return operandType
                is OperandType.Reg
                or OperandType.DerefSizePgRegPlusSImm or OperandType.DerefBytePgRegPlusSImm or OperandType.DerefWordPgRegPlusSImm
                or OperandType.DerefSizePgReg or OperandType.DerefBytePgReg or OperandType.DerefWordPgReg
                or OperandType.DerefSizePgUImm or OperandType.DerefBytePgUImm or OperandType.DerefWordPgUImm;
        }

        private static bool IsImmRefOperand(OperandType operandType)
        {
            return operandType
                is OperandType.Imm
                or OperandType.DerefSizePgRegPlusSImm or OperandType.DerefBytePgRegPlusSImm or OperandType.DerefWordPgRegPlusSImm
                or OperandType.DerefSizePgUImm or OperandType.DerefBytePgUImm or OperandType.DerefWordPgUImm;
        }
    }
}
