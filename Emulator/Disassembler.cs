﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace Emulator
{
    public sealed class Disassembler
    {
        private readonly Dictionary<int, MicrocodeRom.Opcode> opcodeMetadata;
        private readonly Machine machine;

        public Disassembler(MicrocodeRom microcodeRom, Machine machine)
        {
            opcodeMetadata = microcodeRom?.OpcodeMetadata?.ToDictionary(
                x => (x.Value.Index << 8) | (x.Value.OperandCount),
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

            int opcodeIndex = (iword & 0x7C00) >> 10;
            int operandCount;
            if ((iword & 0x8000) != 0)
                operandCount = 1;
            else if ((iword & 0x0300) == 0)
                operandCount = 0;
            else
                operandCount = 2;

            var idx = (opcodeIndex << 8) | operandCount;
            if (!opcodeMetadata.TryGetValue(idx, out var metadata))
                return $"; UNK {iword:X4}";

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

                    case OperandType.Imm16:
                    {
                        operandA = $"0x{machine.ReadWord(segment, offset):X4}";
                        offset += 2;
                        break;
                    }

                    case OperandType.DerefWordImm16:
                    case OperandType.DerefByteImm16:
                    {
                        var busWidthName = operand1Type == OperandType.DerefWordImm16 ? "WORD" : "BYTE";
                        operandA = $"{busWidthName} [0x{machine.ReadWord(segment, offset):X4}]";
                        offset += 2;
                        break;
                    }

                    case OperandType.DerefWordRegPlusImm16:
                    case OperandType.DerefByteRegPlusImm16:
                    {
                        var busWidthName = operand1Type == OperandType.DerefWordRegPlusImm16 ? "WORD" : "BYTE";
                        var regName = ParseRegisterName(iword & 0x000F);
                        //offset += 1;
                        var immValue = (short)-machine.ReadWord(segment, offset);
                        offset += 2;
                        if (immValue == 0)
                            operandA = $"{busWidthName} [{regName}]";
                        else
                        {
                            var signStr = (immValue & 0x8000) != 0 ? "" : "+";
                            operandA = $"{busWidthName} [{regName}{signStr}{immValue}]";
                        }
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

                    case OperandType.Imm16:
                    {
                        operandB = $"0x{machine.ReadWord(segment, offset):X4}";
                        offset += 2;
                        break;
                    }

                    case OperandType.DerefWordImm16:
                    case OperandType.DerefByteImm16:
                    {
                        var busWidthName = operand2Type == OperandType.DerefWordImm16 ? "WORD" : "BYTE";
                        operandB = $"{busWidthName} [0x{machine.ReadWord(segment, offset):X4}]";
                        offset += 2;
                        break;
                    }

                    case OperandType.DerefWordRegPlusImm16:
                    case OperandType.DerefByteRegPlusImm16:
                    {
                        var busWidthName = operand2Type == OperandType.DerefWordRegPlusImm16 ? "WORD" : "BYTE";
                        var regName = ParseRegisterName(machine.ReadByte(segment, offset) & 0x0F);
                        offset += 1;
                        var immValue = (short)-machine.ReadWord(segment, offset);
                        offset += 2;
                        if (immValue == 0)
                            operandB = $"{busWidthName} [{regName}]";
                        else
                        {
                            var signStr = (immValue & 0x8000) != 0 ? "" : "+";
                            operandB = $"{busWidthName} [{regName}{signStr}{immValue}]";
                        }
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
    }
}
