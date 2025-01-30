using System.Diagnostics;
using System.Text;
using DiskUtil;

namespace Emulator
{
    public static class Microcode
    {
        public static MicrocodeRom AssembleRom(string microcodeFilePath)
        {
            var macros = new Dictionary<string, Procedure>();
            var procedures = new List<Procedure>();
            int revision = -1;

            var lines = File.ReadAllLines(microcodeFilePath);
            Procedure? current = null;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                
                var semiIdx = line.IndexOf(';');
                if (semiIdx >= 0)
                    line = line[..semiIdx];

                line = line.Trim();

                if (string.IsNullOrEmpty(line))
                    continue;

                var parts = line.Split(' ', '\t');
                if (current == null)
                {
                    // MACRO DECLARATION
                    if (parts[0] == "#")
                    {
                        var macroName = parts[1];
                        if (macros.ContainsKey(macroName))
                            throw new AssemblyException(i, $"Macro '{macroName}' already declared");

                        current = new Procedure { Name = macroName };
                        macros[macroName] = current;
                        continue;
                    }

                    // REVISION
                    if (parts[0] == "revision")
                    {
                        revision = int.Parse(parts[1]);
                        continue;
                    }

                    // OPCODE DECLARATION
                    var operandCount = Convert.ToInt32(parts[0], 10);
                    var opcodeIndex = Convert.ToInt32(parts[1], 2) & 0x1F;
                    var opcodeName = parts[2].ToUpperInvariant();
                    var operandOrder = false;
                    var operand1 = OperandType.None;
                    var operand2 = OperandType.None;
                    var operand3 = OperandType.None;

                    if (operandCount == 0)
                    {
                        opcodeIndex <<= 2;
                    }
                    else if (operandCount == 1)
                    {
                        opcodeIndex |= 0x20;
                        operand1 = ParseOperand(parts[3], i);
                    }
                    else if (operandCount == 2)
                    {
                        opcodeIndex |= 0x20;
                        operand1 = ParseOperand(parts[3], i);
                        operand2 = ParseOperand(parts[4], i);
                    }
                    else if (operandCount == 3)
                    {
                        opcodeIndex |= 0x20;
                        operand1 = ParseOperand(parts[3], i);
                        operand2 = ParseOperand(parts[4], i);
                        operand3 = ParseOperand(parts[5], i);

                        if (operand1 != operand2)
                            throw new AssemblyException(i, $"Illegal operand combination: {operand1}, {operand2}, {operand3}");
                    }
                    else
                        throw new AssemblyException(i, $"Illegal operand count {operandCount}");

                    current = new Procedure
                    {
                        DeclarationLine = i,
                        Name = opcodeName,
                        Index = opcodeIndex,
                        OperandOrder = operandOrder,
                        Operand1 = operand1,
                        Operand2 = operand2,
                        Operand3 = operand3
                    };
                    
                    procedures.Add(current);
                }
                else
                {
                    var word = ControlWord.None;
                    for (var p = 0; p < parts.Length; p++)
                    {
                        var part = parts[p];
                        if (part == "END")
                        {
                            foreach (var fixup in current.LabelFixups)
                            {
                                var fixupAddress = fixup.Key;
                                var labelName = fixup.Value;
                                if (!current.Labels.TryGetValue(labelName, out var labelAddress))
                                    throw new AssemblyException(i, $"Reference to undeclared label '{labelName}'");

                                current.Code[fixupAddress] |= (ControlWord)((labelAddress << 18) & (int)ControlWord.MASK_OPR);
                            }

                            current = null;
                            break;
                        }

                        // MACRO
                        if (part[0] == '#')
                        {
                            if (parts.Length > 1)
                                throw new AssemblyException(i, "Macro cannot be mixed in with control word");

                            var macroName = part[1..];
                            if (!macros.TryGetValue(macroName, out var macro))
                                throw new AssemblyException(i, $"Unknown macro '{macroName}'");

                            var macroCode = macro.Code;
                            var macroCodeLength = macro.CodeLength;
                            for (int j = 0; j < macroCodeLength; ++j)
                            {
                                var k = current.CodeLength++;
                                if (k >= Procedure.MaxMicrocodeCount)
                                    throw new AssemblyException(i, $"Microcode exceeds {Procedure.MaxMicrocodeCount} words");

                                current.Code[k] = macroCode[j];
                            }

                            break;
                        }

                        // LABELS
                        if (part[^1] == ':')
                        {
                            var labelName = part[..^1];
                            if (current.Labels.ContainsKey(labelName))
                                throw new AssemblyException(i, $"Redeclaration of label '{labelName}'");

                            current.Labels.Add(labelName, current.CodeLength - 1);
                            continue;
                        }

                        if (part[0] == '@')
                        {
                            var labelName = part[1..];
                            if (!current.Labels.TryGetValue(labelName, out var labelAddress))
                                current.LabelFixups[current.CodeLength] = labelName;
                            if ((word & ControlWord.MASK_OPR) != 0)
                                throw new AssemblyException(i, $"Cannot reference label '{labelName}' here");

                            word |= (ControlWord)((labelAddress << 18) & (int)ControlWord.MASK_OPR);
                            continue;
                        }

                        // CONTROL WORDS
                        var subParts = part.Split(':');
                        for (var j = 0; j < subParts.Length; ++j)
                        {
                            var subPart = subParts[j];
                            if (subPart[0] == '$')
                            {
                                subPart = subPart[1..];
                                word |= ControlWord.ADDR;

                                subPart = subPart switch
                                {
                                    "RSO1" => "aRSO1",
                                    "RSO2" => "aRSO2",
                                    "RSO3" => "aRSO3",
                                    "TBO" => "aTBO",
                                    "TCO" => "aTCO",
                                    _ => subPart
                                };
                            }

                            ControlWord cwPart;
                            try
                            {
                                cwPart = subPart switch
                                {
                                    "0" => ControlWord.None,
                                    "1" => ControlWord.Const1,
                                    "2" => ControlWord.Const2,
                                    "4" => ControlWord.Const4,
                                    "RSO1" => IsAluOp(parts, p + 1) ? ControlWord.aRSO1 : ControlWord.bRSO1,
                                    "RSO2" => IsAluOp(parts, p + 1) ? ControlWord.aRSO2 : ControlWord.bRSO2,
                                    "RSO3" => IsAluOp(parts, p + 1) ? ControlWord.aRSO3 : ControlWord.aRSO3,
                                    "TBO" => IsAluOp(parts, p + 1) ? ControlWord.aTBO : ControlWord.bTBO,
                                    "TCO" => IsAluOp(parts, p + 1) ? ControlWord.aTCO : ControlWord.bTCO,
                                    _ => Enum.Parse<ControlWord>(subPart)
                                };
                            }
                            catch (Exception ex)
                            {
                                throw new AssemblyException(i, ex.Message);
                            }

                            if ((word & cwPart) != 0)
                                throw new AssemblyException(i, $"Control signal {subPart} conflicts with another in this word");

                            // TODO Validate that multiple bus OUTs aren't in a single word

                            word |= cwPart;
                        }
                    }

                    if (current == null || word == ControlWord.None)
                        continue; // END

                    var l = current.CodeLength++;
                    if (l >= Procedure.MaxMicrocodeCount)
                        throw new AssemblyException(i, $"Microcode exceeds {Procedure.MaxMicrocodeCount} words");
                    
                    current.Code[l] = word;
                }
            }

            // PROCESS PROCEDURES INTO OPCODES
            var opcodes = new Dictionary<int, Procedure>();
            var opcodesMetadata = new Dictionary<string, MicrocodeRom.Opcode>();
            for (var i = 0; i < procedures.Count; ++i)
            {
                var procedure = procedures[i];

                if (procedure.Operand1 == OperandType.Imm)
                {
                    procedures.Add(procedure with { Operand1 = OperandType.Imm8, Code = ConcretizeMacroCode(procedure, (ControlWord.SIZ1, ControlWord.BYTE), (ControlWord.IPCSIZ1, ControlWord.IPC1)) });
                    procedures.Add(procedure with { Operand1 = OperandType.Imm16, Code = ConcretizeMacroCode(procedure, (ControlWord.SIZ1, ControlWord.WORD), (ControlWord.IPCSIZ1, ControlWord.IPC2)) });
                    continue;
                }
                else if (procedure.Operand1 == OperandType.DerefSizePgRegPlusSImm)
                {
                    procedures.Add(procedure with { Operand1 = OperandType.DerefBytePgRegPlusSImm, Code = ConcretizeMacroCode(procedure, (ControlWord.SIZ1, ControlWord.BYTE), (ControlWord.IPCSIZ1, ControlWord.IPC1)) });
                    procedures.Add(procedure with { Operand1 = OperandType.DerefWordPgRegPlusSImm, Code = ConcretizeMacroCode(procedure, (ControlWord.SIZ1, ControlWord.WORD), (ControlWord.IPCSIZ1, ControlWord.IPC2)) });
                    continue;
                }
                else if (procedure.Operand1 == OperandType.DerefSizePgReg)
                {
                    procedures.Add(procedure with { Operand1 = OperandType.DerefBytePgReg, Code = ConcretizeMacroCode(procedure, (ControlWord.SIZ1, ControlWord.BYTE), (ControlWord.IPCSIZ1, ControlWord.IPC1)) });
                    procedures.Add(procedure with { Operand1 = OperandType.DerefWordPgReg, Code = ConcretizeMacroCode(procedure, (ControlWord.SIZ1, ControlWord.WORD), (ControlWord.IPCSIZ1, ControlWord.IPC2)) });
                    continue;
                }
                else if (procedure.Operand2 == OperandType.Imm)
                {
                    procedures.Add(procedure with { Operand2 = OperandType.Imm8, Code = ConcretizeMacroCode(procedure, (ControlWord.SIZ2, ControlWord.BYTE), (ControlWord.IPCSIZ2, ControlWord.IPC1)) });
                    procedures.Add(procedure with { Operand2 = OperandType.Imm16, Code = ConcretizeMacroCode(procedure, (ControlWord.SIZ2, ControlWord.WORD), (ControlWord.IPCSIZ2, ControlWord.IPC2)) });
                    continue;
                }
                else if (procedure.Operand2 == OperandType.DerefSizePgRegPlusSImm)
                {
                    procedures.Add(procedure with { Operand2 = OperandType.DerefBytePgRegPlusSImm, Code = ConcretizeMacroCode(procedure, (ControlWord.SIZ2, ControlWord.BYTE), (ControlWord.IPCSIZ2, ControlWord.IPC1)) });
                    procedures.Add(procedure with { Operand2 = OperandType.DerefWordPgRegPlusSImm, Code = ConcretizeMacroCode(procedure, (ControlWord.SIZ2, ControlWord.WORD), (ControlWord.IPCSIZ2, ControlWord.IPC2)) });
                    continue;
                }
                else if (procedure.Operand2 == OperandType.DerefSizePgReg)
                {
                    procedures.Add(procedure with { Operand2 = OperandType.DerefBytePgReg, Code = ConcretizeMacroCode(procedure, (ControlWord.SIZ2, ControlWord.BYTE), (ControlWord.IPCSIZ2, ControlWord.IPC1)) });
                    procedures.Add(procedure with { Operand2 = OperandType.DerefWordPgReg, Code = ConcretizeMacroCode(procedure, (ControlWord.SIZ2, ControlWord.WORD), (ControlWord.IPCSIZ2, ControlWord.IPC2)) });
                    continue;
                }

                // TODO Validate that SIZ* macro controlwords are not present after processing

                var addr = procedure.Index << 9;
                int operandCount;

                if (procedure.Operand1 != OperandType.None
                &&  procedure.Operand2 == OperandType.None
                &&  procedure.Operand3 == OperandType.None)
                {
                    addr |= ((int)procedure.Operand1 & 0x07) << 6;
                    operandCount = 1;
                }
                else if (procedure.Operand1 != OperandType.None
                     &&  procedure.Operand2 != OperandType.None
                     &&  procedure.Operand3 == OperandType.None)
                {
                    addr |= ((int)procedure.Operand1 & 0x07) << 6;
                    addr |= ((int)procedure.Operand2 & 0x07) << 3;
                    operandCount = 2;
                }
                else if (procedure.Operand1 != OperandType.None
                     &&  procedure.Operand2 != OperandType.None
                     &&  procedure.Operand3 != OperandType.None)
                {
                    addr |= ((int)procedure.Operand1 & 0x07) << 6;
                    addr |= ((int)procedure.Operand3 & 0x07) << 3;
                    operandCount = 3;
                }
                else
                    operandCount = 0;

                if (!opcodes.TryAdd(addr, procedure))
                    throw new AssemblyException(i, $"Opcode '{procedure.Name} {procedure.Operand1} {procedure.Operand2}' is already declared without wildcard");

                if (!opcodesMetadata.TryGetValue(procedure.Name, out var opcodeMetadata))
                {
                    opcodeMetadata = new MicrocodeRom.Opcode
                    {
                        Name = procedure.Name,
                        Index = procedure.Index,
                        OperandCount =  operandCount,
                        OperandCombinations = new List<MicrocodeRom.Opcode.OperandCombination>()
                    };
                    opcodesMetadata.Add(procedure.Name, opcodeMetadata);
                }

                opcodeMetadata.OperandCombinations.Add(new MicrocodeRom.Opcode.OperandCombination(
                    procedure.OperandOrder,
                    procedure.Operand1,
                    procedure.Operand3 == OperandType.None ? procedure.Operand2 : procedure.Operand3
                ));
            }

            // SERIALIZE OPCODES & MICROCODE
            var microcode = new ControlWord[MicrocodeRom.MaxControlWordCount];
            for (int addr = 0x0000; addr <= 0x7FF8; addr += 0x08)
            {
                if (!opcodes.TryGetValue(addr, out var proc))
                    proc = macros["ILLEGAL"] ?? throw new AssemblyException(-1, "Missing 'ILLEGAL' macro");

                var code = proc.Code;
                var codeLength = proc.CodeLength;
                for (int i = 0; i < codeLength; ++i)
                {
                    var romAddr = (addr | i);
                    microcode[romAddr] = code[i];
                }
            }

            return new MicrocodeRom
            {
                FileVersion = MicrocodeRom.CurrentFileVersion,
                Revision = revision,
                RevisionTs = DateTime.UtcNow,
                Microcode = microcode,
                OpcodeMetadata = opcodesMetadata
            };
        }

        private static OperandType ParseOperand(string value, int line)
        {
            return value switch
            {
                "REG" => OperandType.Reg,
                "IMM8" => OperandType.Imm8,
                "IMM16" => OperandType.Imm16,
                "BYTE[SEG:REG+sIMM]" => OperandType.DerefBytePgRegPlusSImm,
                "WORD[SEG:REG+sIMM]" => OperandType.DerefWordPgRegPlusSImm,
                "BYTE[SEG:REG]" => OperandType.DerefBytePgReg,
                "WORD[SEG:REG]" => OperandType.DerefWordPgReg,
                "[SEG:uIMM16]" => OperandType.DerefPgUImm16,

                "IMM" => OperandType.Imm,
                "SIZE[SEG:REG+sIMM]" => OperandType.DerefSizePgRegPlusSImm,
                "SIZE[SEG:REG]" => OperandType.DerefSizePgReg,
                
                _ => throw new AssemblyException(line, $"Illegal operand: {value}")
            };
        }

        private static bool IsAluOp(string[] parts, int i)
        {
            return i < parts.Length && (Enum.Parse<ControlWord>(parts[i]) & ControlWord.MASK_ALU) != 0;
        }

        private static ControlWord[] ConcretizeMacroCode(
            Procedure procedure,
            params (ControlWord mask, ControlWord value)[] kvps
        )
        {
            var code = new ControlWord[procedure.CodeLength];
            Array.Copy(procedure.Code, code, code.Length);

            for (int i = 0; i < code.Length; ++i)
            {
                var cword = code[i];
                for (var j = 0; j < kvps.Length; ++j)
                {
                    var kvp = kvps[j];
                    if ((cword & kvp.mask) == kvp.mask)
                    {
                        cword &= ~kvp.mask;
                        cword |= kvp.value;
                    }
                }

                code[i] = cword;
            }

            return code;
        }

        private sealed record Procedure
        {
            public const int MaxMicrocodeCount = 8;

            public int DeclarationLine { get; init; }

            public string Name { get; init; }

            public int Index { get; init; }

            public bool OperandOrder { get; init; } // AB = false; BA = true

            public OperandType Operand1 { get; init; }

            public OperandType Operand2 { get; init;  }

            public OperandType Operand3 { get; init;  }

            public ControlWord[] Code { get; init; }

            public int CodeLength { get; set; }

            public Dictionary<string, int> Labels { get; }

            public Dictionary<int, string> LabelFixups { get; }

            public Procedure()
            {
                Code = new ControlWord[MaxMicrocodeCount];
                CodeLength = 0;
                Labels = new Dictionary<string, int>();
                LabelFixups = new Dictionary<int, string>();
            }
        }
    }

    public enum OperandType
    {
        Reg,
        Imm8,
        Imm16,
        DerefBytePgRegPlusSImm,
        DerefWordPgRegPlusSImm,
        DerefBytePgReg,
        DerefWordPgReg,
        DerefPgUImm16,

        None,
        Imm,
        DerefSizePgRegPlusSImm,
        DerefSizePgReg
    }

    public sealed class MicrocodeRom
    {
        public const int Magic = 0x52434D43;
        public const int CurrentFileVersion = 1;
        public const int MaxControlWordCount = 0x7FFF; // 8 microcode instructions per opcode
        public const int BytesPerControlWord = 4;

        public int FileVersion { get; init; }

        public int Revision { get; init; }

        public DateTime RevisionTs { get; init; }

        public ControlWord[] Microcode { get; init; }

        public Dictionary<string, Opcode> OpcodeMetadata { get; init; }

        public static byte[] ToRomBinary(MicrocodeRom rom)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            writer.Write(Magic);
            writer.Write((byte)CurrentFileVersion);
            writer.Write((short)rom.Revision);
            writer.Write((int)rom.RevisionTs.ToCobaltTime());
            writer.Write((short)rom.OpcodeMetadata.Count);
            writer.Write((byte)BytesPerControlWord);
            writer.Write((int)MaxControlWordCount);
            writer.BaseStream.Position += 14;

            var result = new byte[MaxControlWordCount * BytesPerControlWord];
            for (int i = 0; i < rom.Microcode.Length; ++i)
            {
                var microcode = (int)rom.Microcode[i];
                var romAddr = i * BytesPerControlWord;
                for (int j = 0; j < BytesPerControlWord; ++j)
                {
                    result[romAddr + j] = (byte)(microcode & 0xFF);
                    microcode >>= 8;
                }
            }

            writer.Write(result);

            foreach (var opcodeMetadata in rom.OpcodeMetadata.Values)
            {
                writer.Write(opcodeMetadata.Name);
                writer.Write((byte)opcodeMetadata.Index);
                writer.Write((byte)opcodeMetadata.OperandCount);
                writer.Write((byte)opcodeMetadata.OperandCombinations.Count);
                foreach (var operandCombination in opcodeMetadata.OperandCombinations)
                {
                    writer.Write((byte)(
                          (operandCombination.RTL ? 0x80 : 0)
                        | (((int)operandCombination.A & 0x07) << 4)
                        | ((int)operandCombination.B & 0x07)
                    ));
                }
            }

            return stream.ToArray();
        }

        public static MicrocodeRom? FromRomBinary(byte[] rom)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            
            using var stream = new MemoryStream(rom);
            using var reader = new BinaryReader(stream);

            if (reader.ReadInt32() != Magic)
                return null;

            var fileVersion = reader.ReadByte();
            var revision = reader.ReadInt16();
            var revisionTs = reader.ReadInt32().FromCobaltTime();
            var opcodeCount = reader.ReadInt16();
            var bankCount = reader.ReadByte();
            var bytesPerBank = reader.ReadInt32();
            reader.BaseStream.Position += 14;

            var microcodeBytes = new byte[bytesPerBank * bankCount];
            int i = 0;
            while (i < microcodeBytes.Length)
                i += reader.Read(microcodeBytes, i, microcodeBytes.Length - i);

            var microcode = new ControlWord[bytesPerBank];
            for (i = 0; i < MaxControlWordCount; ++i)
            {
                int cword = 0;
                var romAddr = i * BytesPerControlWord;
                for (int j = BytesPerControlWord - 1; j >= 0; --j)
                {
                    cword |= microcodeBytes[romAddr + j];
                    cword <<= 8;
                }

                microcode[i] = (ControlWord)cword;
            }

            var opcodeMetadata = new Dictionary<string, Opcode>();
            for (i = 0; i < opcodeCount; ++i)
            {
                var opcodeName = reader.ReadString();
                var opcodeIndex = reader.ReadByte();
                var operandCount = reader.ReadByte();
                var combinationCount = reader.ReadByte();
                var operandCombinations = new List<Opcode.OperandCombination>();
                for (int j = 0; j < combinationCount; ++j)
                {
                    var value = reader.ReadByte();
                    operandCombinations[j] = new Opcode.OperandCombination(
                        (value & 0x80) == 0x80,
                        (OperandType)((value & 0x70) >> 4),
                        (OperandType)(value & 0x07)
                    );
                }

                opcodeMetadata[opcodeName] = new Opcode
                {
                    Name = opcodeName,
                    Index = opcodeIndex,
                    OperandCount = operandCount,
                    OperandCombinations = operandCombinations
                };
            }

            return new MicrocodeRom
            {
                FileVersion = fileVersion,
                Revision = revision,
                RevisionTs = revisionTs,
                Microcode = microcode,
                OpcodeMetadata = opcodeMetadata
            };
        }

        public sealed class Opcode
        {
            public string Name { get; init; }

            public int Index { get; init; }
            
            public int OperandCount { get; init; }

            public List<OperandCombination> OperandCombinations { get; init; }

            public sealed record OperandCombination(bool RTL, OperandType A, OperandType B);
        }
    }

    [Flags]
    public enum ControlWord : ulong
    {
        None,

        IPC1        = 0b00000000_00000000_00000000_00000001,
        IPC2        = 0b00000000_00000000_00000000_00000010,
        IPC3        = 0b00000000_00000000_00000000_00000011,
        IPC4        = 0b00000000_00000000_00000000_00000100,
        IPCORW1     = 0b00000000_00000000_00000000_00000101,
        IPCORW2     = 0b00000000_00000000_00000000_00000110,
        JMP         = 0b00000000_00000000_00000000_00000111,
        MASK_IPC    = 0b00000000_00000000_00000000_00000111,

        II          = 0b00000000_00000000_00000000_00001000,
        FI          = 0b00000000_00000000_00000000_00010000,
        INTENLATCH  = 0b00000000_00000000_00000000_00011000,
        MASK_IR     = 0b00000000_00000000_00000000_00011000,
        
        aRSO1       = 0b00000000_00000000_00000000_00100000,
        aRSO2       = 0b00000000_00000000_00000000_01000000,
        aRSO3       = 0b00000000_00000000_00000000_01100000,
        TAO         = 0b00000000_00000000_00000000_10000000,
        aTBO        = 0b00000000_00000000_00000000_10100000,
        aTCO        = 0b00000000_00000000_00000000_11000000,
        SPO         = 0b00000000_00000000_00000000_11100000,
        MASK_A      = 0b00000000_00000000_00000000_11100000,

        bRSO2       = 0b00000000_00000000_00000001_00000000,
        bRSO1       = 0b00000000_00000000_00000010_00000000,
        bTBO        = 0b00000000_00000000_00000011_00000000,
        bTCO        = 0b00000000_00000000_00000100_00000000,
        FO          = 0b00000000_00000000_00000101_00000000,
        Const2      = 0b00000000_00000000_00000110_00000000,
        Const4      = 0b00000000_00000000_00000111_00000000,
        MASK_B      = 0b00000000_00000000_00000111_00000000,
        
        RSI1        = 0b00000000_00000000_00001000_00000000,
        RSI2        = 0b00000000_00000000_00010000_00000000,
        RSI3        = 0b00000000_00000000_00011000_00000000,
        TAI         = 0b00000000_00000000_00100000_00000000,
        TBI         = 0b00000000_00000000_00101000_00000000,
        TCI         = 0b00000000_00000000_00110000_00000000,
        SPI         = 0b00000000_00000000_00111000_00000000,
        MASK_RI     = 0b00000000_00000000_00111000_00000000,
        
        R           = 0b00000000_00000000_01000000_00000000,
        W           = 0b00000000_00000000_10000000_00000000,

        BYTE        = 0b00000000_00000000_00000000_00000000,
        WORD        = 0b00000000_00000001_00000000_00000000,
        DWORD       = 0b00000000_00000010_00000000_00000000,
        BUSW_XX_1   = 0b00000000_00000011_00000000_00000000,
        MASK_BUSW   = 0b00000000_00000011_00000000_00000000,

        ADD         = 0b00000000_00000100_00000000_00000000,
        SUB         = 0b00000000_00001000_00000000_00000000,
        OR          = 0b00000000_00001100_00000000_00000000,
        XOR         = 0b00000000_00010000_00000000_00000000,
        AND         = 0b00000000_00010100_00000000_00000000,
        ROL         = 0b00000000_00011000_00000000_00000000,
        ROR         = 0b00000000_00011100_00000000_00000000,
        SHL         = 0b00000000_00100000_00000000_00000000,
        SHR         = 0b00000000_00100100_00000000_00000000,
        ALU_XX_1    = 0b00000000_00101000_00000000_00000000,
        ALU_XX_2    = 0b00000000_00101100_00000000_00000000,
        ALU_XX_3    = 0b00000000_00110000_00000000_00000000,
        ALU_XX_4    = 0b00000000_00110100_00000000_00000000,
        ALU_XX_5    = 0b00000000_00111000_00000000_00000000,
        ALU_XX_6    = 0b00000000_00111100_00000000_00000000,
        MASK_ALU    = 0b00000000_00111100_00000000_00000000,
        MASK_OPR    = 0b00000000_00111100_00000000_00000000,
        
        DATA        = 0b00000000_00000000_00000000_00000000,
        ADDR        = 0b00000000_01000000_00000000_00000000,

        CG          = 0b00000000_10000000_00000000_00000000,
        SG          = 0b00000001_00000000_00000000_00000000,
        PAG1        = 0b00000001_10000000_00000000_00000000,
        PAG2        = 0b00000010_00000000_00000000_00000000,
        PAG3        = 0b00000010_10000000_00000000_00000000,
        INTLATCH    = 0b00000011_00000000_00000000_00000000,
        LI16        = 0b00000011_10000000_00000000_00000000,
        MASK_SEG    = 0b00000011_10000000_00000000_00000000,
        
        JNF         = 0b00000100_00000000_00000000_00000000,
        JC          = 0b00001000_00000000_00000000_00000000,
        LNZ         = 0b00001100_00000000_00000000_00000000,
        MASK_CMJ    = 0b00001100_00000000_00000000_00000000,

        Const1      = 0b00010000_00000000_00000000_00000000,
        TGC         = 0b00100000_00000000_00000000_00000000,

        IPO         = 0b01000000_00000000_00000000_00000000,
        HLT         = 0b10000000_00000000_00000000_00000000,
        RTN         = 0b11000000_00000000_00000000_00000000,
        MASK_IP     = 0b11000000_00000000_00000000_00000000,

        // NOTE Not real control words
        SIZ1        = 0b00000001_00000000_00000000_00000000_00000000,
        SIZ2        = 0b00000010_00000000_00000000_00000000_00000000,
        IPCSIZ1     = 0b00000100_00000000_00000000_00000000_00000000,
        IPCSIZ2     = 0b00001000_00000000_00000000_00000000_00000000,
    }

    public enum Conditional : byte
    {
        None        = 0b0000,
        EQ          = 0b0001,
        NEQ         = 0b0010,
        GTu         = 0b0011,
        GTEu        = 0b0100,
        LTu         = 0b0101,
        LTEu        = 0b0110,
        GTs         = 0b0111,
        GTEs        = 0b1000,
        LTs         = 0b1001,
        LTEs        = 0b1010,
        COND_XX_1   = 0b1011,
        COND_XX_2   = 0b1100,
        COND_XX_3   = 0b1101,
        fIMM3       = 0b1110,
        f32         = 0b1111,

        Z = EQ,
        NZ = NEQ
    }

    public static class MicrocodeUtility
    {
        public static string Disassemble(this ControlWord cw)
        {
            var sb = new StringBuilder();

            var isRead = (cw & ControlWord.R) != 0;
            var isWrite = (cw & ControlWord.W) != 0;
            if (isRead)
                sb.Append("R ");
            if (isWrite)
                sb.Append("W ");
            if (isRead || isWrite)
            {
                if ((cw & ControlWord.MASK_BUSW) == ControlWord.BYTE)
                    sb.Append("BYTE ");
                else if ((cw & ControlWord.MASK_BUSW) == ControlWord.WORD)
                    sb.Append("WORD ");
                else if ((cw & ControlWord.MASK_BUSW) == ControlWord.DWORD)
                    sb.Append("DWORD ");
            }

            if ((cw & ControlWord.MASK_SEG) == ControlWord.CG)
                sb.Append("CG:");
            else if ((cw & ControlWord.MASK_SEG) == ControlWord.SG)
                sb.Append("SG:");
            else if ((cw & ControlWord.MASK_SEG) == ControlWord.PAG1)
                sb.Append("PAG1:");
            else if ((cw & ControlWord.MASK_SEG) == ControlWord.PAG2)
                sb.Append("PAG2:");
            else if ((cw & ControlWord.MASK_SEG) == ControlWord.INTLATCH)
                sb.Append("INTLATCH ");
            else if ((cw & ControlWord.MASK_SEG) == ControlWord.LI16)
                sb.Append("LI16 ");

            if ((cw & ControlWord.ADDR) != 0)
                sb.Append("$");

            if ((cw & ControlWord.MASK_IP) == ControlWord.IPO)
                sb.Append("IPO ");
            if ((cw & ControlWord.MASK_IP) == ControlWord.HLT)
                sb.Append("HLT ");
            else if ((cw & ControlWord.MASK_IP) == ControlWord.RTN)
                sb.Append("RTN ");

            if ((cw & ControlWord.MASK_A) == ControlWord.aRSO1)
                sb.Append("RSO1 ");
            else if ((cw & ControlWord.MASK_A) == ControlWord.aRSO2)
                sb.Append("RSO2 ");
            else if ((cw & ControlWord.MASK_A) == ControlWord.aRSO3)
                sb.Append("RSO3 ");
            else if ((cw & ControlWord.MASK_A) == ControlWord.TAO)
                sb.Append("TAO ");
            else if ((cw & ControlWord.MASK_A) == ControlWord.aTBO)
                sb.Append("TBO ");
            else if ((cw & ControlWord.MASK_A) == ControlWord.aTCO)
                sb.Append("TCO ");
            else if ((cw & ControlWord.MASK_A) == ControlWord.SPO)
                sb.Append("SPO ");

            var isALUOperation = (cw & ControlWord.MASK_ALU) != 0 && (cw & ControlWord.MASK_CMJ) == 0;
            if (isALUOperation)
            {
                if ((cw & ControlWord.MASK_ALU) == ControlWord.ADD)
                    sb.Append("ADD ");
                else if ((cw & ControlWord.MASK_ALU) == ControlWord.SUB)
                    sb.Append("SUB ");
                else if ((cw & ControlWord.MASK_ALU) == ControlWord.OR)
                    sb.Append("OR ");
                else if ((cw & ControlWord.MASK_ALU) == ControlWord.XOR)
                    sb.Append("XOR ");
                else if ((cw & ControlWord.MASK_ALU) == ControlWord.AND)
                    sb.Append("AND ");
                else if ((cw & ControlWord.MASK_ALU) == ControlWord.ROL)
                    sb.Append("ROL ");
                else if ((cw & ControlWord.MASK_ALU) == ControlWord.ROR)
                    sb.Append("ROR ");
                else if ((cw & ControlWord.MASK_ALU) == ControlWord.SHL)
                    sb.Append("SHL ");
                else if ((cw & ControlWord.MASK_ALU) == ControlWord.SHR)
                    sb.Append("SHR ");
            }

            if ((cw & ControlWord.MASK_B) == ControlWord.bRSO2)
                sb.Append("RSO2 ");
            else if ((cw & ControlWord.MASK_B) == ControlWord.bRSO1)
                sb.Append("RSO1 ");
            else if ((cw & ControlWord.MASK_B) == ControlWord.bTBO)
                sb.Append("TBO ");
            else if ((cw & ControlWord.MASK_B) == ControlWord.bTCO)
                sb.Append("TCO ");
            else if ((cw & ControlWord.MASK_B) == ControlWord.FO)
                sb.Append("FO ");
            else if ((cw & ControlWord.MASK_B) == ControlWord.Const2)
                sb.Append("2 ");
            else if ((cw & ControlWord.MASK_B) == ControlWord.Const4)
                sb.Append("4 ");

            if ((cw & ControlWord.MASK_RI) == ControlWord.RSI1)
                sb.Append("RSI1 ");
            else if ((cw & ControlWord.MASK_RI) == ControlWord.RSI2)
                sb.Append("RSI2 ");
            else if ((cw & ControlWord.MASK_RI) == ControlWord.RSI3)
                sb.Append("RSI3 ");
            else if ((cw & ControlWord.MASK_RI) == ControlWord.TAI)
                sb.Append("TAI ");
            else if ((cw & ControlWord.MASK_RI) == ControlWord.TBI)
                sb.Append("TBI ");
            else if ((cw & ControlWord.MASK_RI) == ControlWord.TCI)
                sb.Append("TCI ");
            else if ((cw & ControlWord.MASK_RI) == ControlWord.SPI)
                sb.Append("SPI ");

            if ((cw & ControlWord.Const1) == ControlWord.Const1)
                sb.Append("1 ");

            if ((cw & ControlWord.MASK_CMJ) == ControlWord.JNF)
            {
                sb.Append("JNF ");
                sb.Append((int)(cw & ControlWord.MASK_OPR) >> 18);
                sb.Append(' ');
            }
            else if ((cw & ControlWord.MASK_CMJ) == ControlWord.JC)
            {
                sb.Append("JC ");
                sb.Append((int)(cw & ControlWord.MASK_OPR) >> 18);
                sb.Append(' ');
            }
            else if ((cw & ControlWord.MASK_CMJ) == ControlWord.LNZ)
            {
                sb.Append("LNZ ");
                sb.Append((int)(cw & ControlWord.MASK_OPR) >> 18);
                sb.Append(' ');
            }

            if ((cw & ControlWord.MASK_IR) == ControlWord.II)
                sb.Append("II ");
            else if ((cw & ControlWord.MASK_IR) == ControlWord.FI)
                sb.Append("FI ");
            else if ((cw & ControlWord.MASK_IR) == ControlWord.INTENLATCH)
                sb.Append("INTENLATCH ");

            if ((cw & ControlWord.TGC) == ControlWord.TGC)
                sb.Append("TGC ");

            if ((cw & ControlWord.MASK_IPC) == ControlWord.IPC1)
                sb.Append("IPC1");
            else if ((cw & ControlWord.MASK_IPC) == ControlWord.IPC2)
                sb.Append("IPC2");
            else if ((cw & ControlWord.MASK_IPC) == ControlWord.IPC3)
                sb.Append("IPC3");
            else if ((cw & ControlWord.MASK_IPC) == ControlWord.IPC4)
                sb.Append("IPC4");
            else if ((cw & ControlWord.MASK_IPC) == ControlWord.IPCORW1)
                sb.Append("IPCORW1");
            else if ((cw & ControlWord.MASK_IPC) == ControlWord.IPCORW2)
                sb.Append("IPCORW2");
            else if ((cw & ControlWord.MASK_IPC) == ControlWord.JMP)
                sb.Append("JMP");

            return sb.ToString();
        }
    }

    public sealed class AssemblyException : Exception
    {
        public int Line { get; }
            
        public AssemblyException(int line, string message)
            : base($"Line {line + 1}: {message}")
        {
            Line = line + 1;
        }
    }
}
