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
                    var opcodeIndex = Convert.ToInt32(parts[0], 2) & 0x1F;
                    var opcodeName = parts[1].ToUpperInvariant();
                    var operandCount = parts.Length - 2;
                    var operands = new List<OperandType>();

                    for (int j = 0; j < operandCount; ++j)
                    {
                        var operand = ParseOperand(parts[j + 2], i);
                        operands.Add(operand);
                    }
                    
                    // TODO Validate
                    //if (operand1 != operand2)
                    //    throw new AssemblyException(i, $"Illegal operand combination: {operand1}, {operand2}, {operand3}");

                    current = new Procedure
                    {
                        DeclarationLine = i,
                        Name = opcodeName,
                        Index = opcodeIndex,
                        Operands = operands
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
                                    "3" => ControlWord.Const3,
                                    "4" => ControlWord.Const4,
                                    "RSO1" => IsAluOp(parts, p + 1) ? ControlWord.aRSO1 : ControlWord.bRSO1,
                                    "RSO2" => IsAluOp(parts, p + 1) ? ControlWord.aRSO2 : ControlWord.bRSO2,
                                    "RSO3" => IsAluOp(parts, p + 1) ? ControlWord.aRSO3 : throw new AssemblyException(i, "RSO3 is LHS-only"),
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
                var rootProcedure = procedures[i];
                
                int operandCount = rootProcedure.Operands.Count;
                var operandIndices = ResolveOperandIndices(rootProcedure.Operands);
                var jCount = operandIndices?.Count ?? 1;

                for (int j = 0; j < jCount; ++j)
                {
                    var operandIndex = operandIndices?[j];
                    var size = ResolveEncodedInstructionSizeInBytes(rootProcedure.Operands, operandIndex);
                    var code = ConcretizeMacroCode(
                        rootProcedure,
                        (ControlWord.IPCIW, (ControlWord)((int)ControlWord.IPC1 + size))
                    );

                    var procedure = rootProcedure with { Code = code };

                    int addr = 0;
                    if (operandCount == 0)
                        addr |= (procedure.Index & 0x07) << 7;
                    else
                    {
                        addr |= (procedure.Index & 0x1F) << 10;
                        addr |= operandIndex!.Value << 4;
                    }

                    if (!opcodes.TryAdd(addr, procedure))
                        throw new AssemblyException(procedure.DeclarationLine, $"Opcode '{procedure.Name} {string.Join(" ", procedure.Operands)}' is already declared");

                    if (!opcodesMetadata.TryGetValue(procedure.Name, out var opcodeMetadata))
                    {
                        opcodeMetadata = new MicrocodeRom.Opcode
                        {
                            Name = procedure.Name,
                            Index = procedure.Index,
                            OperandCount =  operandCount,
                            OperandCombinations = new List<IReadOnlyList<OperandType>>()
                        };
                        opcodesMetadata.Add(procedure.Name, opcodeMetadata);
                    }

                    opcodeMetadata.OperandCombinations.Add(procedure.Operands);
                }
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

        private static int ResolveEncodedInstructionSizeInBytes(IReadOnlyList<OperandType> operands, int? operandIndex)
        {
            if (operandIndex == null || operands.Count == 0)
                return 1;

            // TIIFFF
            var value = operandIndex.Value;
            var format = OperandFormats[value & 0x07];
            var hasFlag = (value & 0b10000) == 0;

            var regBits = 0;
            var immABits = 0;
            var immBBits = 0;

            foreach (var operand in operands)
            {
                if (IsRegRefOperand(operand))
                    regBits += 4;
                if (immABits == 0 && IsImmRefOperand(operand))
                    immABits += format.LengthA;
                else if (immBBits == 0 && IsImmRefOperand(operand))
                    immBBits += format.LengthB;
            }

            if (hasFlag)
                regBits -= 4;

            var bits = 16 + regBits + immABits + immBBits;
            return bits / 8;
        }

        private static bool IsRegRefOperand(OperandType operandType)
        {
            return operandType
                is OperandType.Reg
                or OperandType.DerefSizePgRegPlusSImm
                or OperandType.DerefSizePgReg
                or OperandType.DerefSizePgUImm;
        }

        private static bool IsImmRefOperand(OperandType operandType)
        {
            return operandType
                is OperandType.Imm
                or OperandType.DerefSizePgRegPlusSImm
                or OperandType.DerefSizePgUImm;
        }

        private static IReadOnlyList<int>? ResolveOperandIndices(IReadOnlyList<OperandType> operands)
        {
            var key = string.Join(' ', operands.Select(x => x switch
            {
                OperandType.Reg => "REG",
                OperandType.Imm => "IMM",
                OperandType.DerefSizePgRegPlusSImm => "SZ[PG:REG+sIMM]",
                OperandType.DerefSizePgReg => "SZ[PG:REG]",
                OperandType.DerefSizePgUImm => "SZ[PG:uIMM]",
                _ => throw new ArgumentOutOfRangeException(nameof(x), x, null)
            }));

            return OperandTable.GetValueOrDefault(key);
        }

        private static Dictionary<string, int[]> OperandTable = new()
        {
            ["REG"] = new[] { 0b000000, 0b100111 },
            ["SZ[PG:REG+sIMM]"] = new[] { 0b001000, 0b000001, 0b100010, 0b100011 },
            ["SZ[PG:REG]"] = new[] { 0b010000, 0b110111 },
            ["SZ[PG:uIMM]"] = new[] { 0b011000, 0b001001, 0b101010, 0b101011 },
            ["IMM"] = new[] { 0b100000, 0b100001, 0b101111 },

            ["REG REG"] = new[] { 0b101000 },
            ["REG IMM"] = new[] { 0b000000, 0b000001, 0b100010, 0b100011 },
            ["REG SZ[PG:REG+sIMM]"] = new[] { 0b101011, 0b100100, 0b000111 },
            ["REG SZ[PG:REG]"] = new[] { 0b111000 },
            ["REG SZ[PG:uIMM]"] = new[] { 0b101100, 0b010101, 0b001111 },

            ["IMM IMM"] = new[] { 0b100001, 0b111111 },

            ["SZ[PG:REG+sIMM] REG"] = new[] { 0b100101 },
            ["SZ[PG:REG+sIMM] IMM"] = new[] { 0b001001, 0b111011, 0b011100, 0b101101, 0b100110, 0b100111 },
            ["SZ[PG:REG+sIMM] SZ[PG:REG+sIMM]"] = new[] { 0b000011, 0b010100, 0b110101, 0b000110, 0b101110, 0b011111 },
            ["SZ[PG:REG+sIMM] SZ[PG:REG]"] = new[] { 0b110001, 0b001011, 0b110110 },
            ["SZ[PG:REG+sIMM] SZ[PG:uIMM]"] = new[] { 0b010010, 0b110011, 0b001100, 0b111101, 0b111110 },

            ["SZ[PG:REG] REG"] = new[] { 0b110000 },
            ["SZ[PG:REG] IMM"] = new[] { 0b001000, 0b010001, 0b101010, 0b101111 },
            ["SZ[PG:REG] SZ[PG:REG+sIMM]"] = new[] { 0b111001, 0b001010, 0b000101 },
            ["SZ[PG:REG] SZ[PG:REG]"] = new[] { 0b100000 },
            ["SZ[PG:REG] SZ[PG:uIMM]"] = new[] { 0b101001, 0b001101 },

            ["SZ[PG:uIMM] REG"] = new[] { 0b000010, 0b010011, 0b110100 },
            ["SZ[PG:uIMM] IMM"] = new[] { 0b010000, 0b011001, 0b110010, 0b011011, 0b010111, 0b110111 },
            ["SZ[PG:uIMM] SZ[PG:REG+sIMM]"] = new[] { 0b111100, 0b011101 },
            ["SZ[PG:uIMM] SZ[PG:REG]"] = new[] { 0b011010 },
            ["SZ[PG:uIMM] SZ[PG:uIMM]"] = new[] { 0b111010, 0b000100 },

            ["REG REG REG"] = new[] { 0b100010, 0b000110 },
            ["REG REG IMM"] = new[] { 0b110011, 0b100100 },
            ["REG REG SZ[PG:REG+sIMM]"] = new[] { 0b101000, 0b100011, 0b000100 },
            ["REG REG SZ[PG:REG]"] = new[] { 0b101011, 0b001101, 0b001110 },
            ["REG REG SZ[PG:uIMM]"] = new[] { 0b100001, 0b111101 },

            ["IMM IMM REG"] = new[] { 0b000000, 0b000001, 0b100111 },
            ["IMM IMM SZ[PG:REG]"] = new[] { 0b001000, 0b001001 },

            ["SZ[PG:REG+sIMM] SZ[PG:REG+sIMM] REG"] = new[] { 0b001100, 0b011101 },
            ["SZ[PG:REG+sIMM] SZ[PG:REG+sIMM] IMM"] = new[] { 0b111011, 0b111100 },
            ["SZ[PG:REG+sIMM] SZ[PG:REG+sIMM] SZ[PG:REG]"] = new[] { 0b001010, 0b010101 },
            ["SZ[PG:REG+sIMM] SZ[PG:REG+sIMM] SZ[PG:uIMM]"] = new[] { 0b000101 },

            ["SZ[PG:REG] SZ[PG:REG] REG"] = new[] { 0b101010 },
            ["SZ[PG:REG] SZ[PG:REG] IMM"] = new[] { 0b000010, 0b101100 },
            ["SZ[PG:REG] SZ[PG:REG] SZ[PG:REG+sIMM]"] = new[] { 0b000011, 0b010100, 0b110100 },
            ["SZ[PG:REG] SZ[PG:REG] SZ[PG:REG]"] = new[] { 0b110010 },
            ["SZ[PG:REG] SZ[PG:REG] SZ[PG:uIMM]"] = new[] { 0b001011, 0b011100 },

            ["SZ[PG:uIMM] SZ[PG:uIMM] REG"] = new[] { 0b101001, 0b100101 },
            ["SZ[PG:uIMM] SZ[PG:uIMM] IMM"] = new[] { 0b101101 },
        };

        private static readonly IReadOnlyList<OperandFormat> OperandFormats = new[]
        {
            new OperandFormat(16, 8, 24, 8),
            new OperandFormat(16, 16, 32, 16),
            new OperandFormat(20, 16, 36, 12),
            new OperandFormat(24, 8, 32, 16),
            new OperandFormat(24, 16, 40, 8),
            new OperandFormat(24, 8, 32, 8),
            new OperandFormat(24, 12, 36, 12),
            new OperandFormat(20, 4, 24, 8),
        };

        public sealed record OperandFormat(int OffsetA, int LengthA, int OffsetB, int LengthB);

        private static OperandType ParseOperand(string value, int line)
        {
            return value switch
            {
                "REG" => OperandType.Reg,
                "IMM" => OperandType.Imm,
                "SZ[PG:REG+sIMM]" => OperandType.DerefSizePgRegPlusSImm,
                "SZ[PG:REG]" => OperandType.DerefSizePgReg,
                "SZ[PG:uIMM]" => OperandType.DerefSizePgUImm,
                
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
            public const int MaxMicrocodeCount = 16;

            public int DeclarationLine { get; init; }

            public string Name { get; init; }

            public int Index { get; init; }

            public IReadOnlyList<OperandType> Operands { get; init; }

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
        None,
        Reg,
        Imm,
        DerefSizePgRegPlusSImm,
        DerefSizePgReg,
        DerefSizePgUImm
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
                    // TODO
                    //writer.Write((byte)(
                    //      (operandCombination.RTL ? 0x80 : 0)
                    //    | (((int)operandCombination.A & 0x07) << 4)
                    //    | ((int)operandCombination.B & 0x07)
                    //));
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
                var operandCombinations = new List<IReadOnlyList<OperandType>>();
                for (int j = 0; j < combinationCount; ++j)
                {
                    var value = reader.ReadByte();
                    // operandCombinations[j] = ResolveOperandIndex()
                    // TODO
                    //operandCombinations[j] = new Opcode.OperandCombination(
                    //    (value & 0x80) == 0x80,
                    //    (OperandType)((value & 0x70) >> 4),
                    //    (OperandType)(value & 0x07)
                    //);
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

            public List<IReadOnlyList<OperandType>> OperandCombinations { get; init; }
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
        IPC5        = 0b00000000_00000000_00000000_00000101,
        IPC6        = 0b00000000_00000000_00000000_00000110,
        JMP         = 0b00000000_00000000_00000000_00000111,
        MASK_IPC    = 0b00000000_00000000_00000000_00000111,

        II          = 0b00000000_00000000_00000000_00001000,
        FI          = 0b00000000_00000000_00000000_00010000,
        FFI         = 0b00000000_00000000_00000000_00011000,
        MASK_IR     = 0b00000000_00000000_00000000_00011000,
        
        aRSO1       = 0b00000000_00000000_00000000_00100000,
        aRSO2       = 0b00000000_00000000_00000000_01000000,
        aRSO3       = 0b00000000_00000000_00000000_01100000,
        TAO         = 0b00000000_00000000_00000000_10000000,
        aTBO        = 0b00000000_00000000_00000000_10100000,
        aTCO        = 0b00000000_00000000_00000000_11000000,
        SPO         = 0b00000000_00000000_00000000_11100000,
        MASK_A      = 0b00000000_00000000_00000000_11100000,

        bRSO1       = 0b00000000_00000000_00000001_00000000,
        bRSO2       = 0b00000000_00000000_00000010_00000000,
        bTBO        = 0b00000000_00000000_00000011_00000000,
        bTCO        = 0b00000000_00000000_00000100_00000000,
        FO          = 0b00000000_00000000_00000101_00000000,
        ISO1        = 0b00000000_00000000_00000110_00000000,
        ISO2        = 0b00000000_00000000_00000111_00000000,
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
        XX          = 0b00000000_00000010_00000000_00000000,
        IWORD       = 0b00000000_00000011_00000000_00000000,
        MASK_BUSW   = 0b00000000_00000011_00000000_00000000,

        ADD         = 0b00000000_00000100_00000000_00000000,
        SUB         = 0b00000000_00001000_00000000_00000000,
        OR          = 0b00000000_00001100_00000000_00000000,
        XOR         = 0b00000000_00010000_00000000_00000000,
        AND         = 0b00000000_00010100_00000000_00000000,
        ROL         = 0b00000000_00011000_00000000_00000000,
        ROR         = 0b00000000_00011100_00000000_00000000,
        MASK_ALU    = 0b00000000_00011100_00000000_00000000,
        MASK_OPR    = 0b00000000_00011100_00000000_00000000,
        
        DATA        = 0b00000000_00000000_00000000_00000000,
        ADDR        = 0b00000000_00100000_00000000_00000000,

        CG          = 0b00000000_01000000_00000000_00000000,
        SG          = 0b00000000_10000000_00000000_00000000,
        PAG1        = 0b00000000_11000000_00000000_00000000,
        PAG2        = 0b00000001_00000000_00000000_00000000,
        PAG3        = 0b00000001_01000000_00000000_00000000,
        INTLATCH    = 0b00000001_10000000_00000000_00000000,
        LI16        = 0b00000001_11000000_00000000_00000000,
        MASK_SEG    = 0b00000001_11000000_00000000_00000000,
        
        JNZ         = 0b00000010_00000000_00000000_00000000,
        JC          = 0b00000100_00000000_00000000_00000000,
        LNZ         = 0b00000110_00000000_00000000_00000000,
        MASK_CMJ    = 0b00000110_00000000_00000000_00000000,

        Const1      = 0b00001000_00000000_00000000_00000000,
        Const2      = 0b00010000_00000000_00000000_00000000,
        Const3      = 0b00011000_00000000_00000000_00000000,
        Const4      = 0b00100000_00000000_00000000_00000000,
        CGI         = 0b00101000_00000000_00000000_00000000,
        TGC         = 0b00110000_00000000_00000000_00000000,
        PRVCHK      = 0b00111000_00000000_00000000_00000000,
        MASK_CONST  = 0b00111000_00000000_00000000_00000000,

        IPO         = 0b01000000_00000000_00000000_00000000,
        HLT         = 0b10000000_00000000_00000000_00000000,
        RTN         = 0b11000000_00000000_00000000_00000000,
        MASK_IP     = 0b11000000_00000000_00000000_00000000,

        // NOTE Not real control words
        SIZ1        = 0b00000001_00000000_00000000_00000000_00000000,
        SIZ2        = 0b00000010_00000000_00000000_00000000_00000000,
        SIZ3        = 0b00000100_00000000_00000000_00000000_00000000,
        IPCIW       = 0b00001000_00000000_00000000_00000000_00000000
    }

    public enum Conditional : byte
    {
        Operandless = 0b0000,
        None        = 0b0001,
        EQ          = 0b0010,
        NEQ         = 0b0011,
        GTu         = 0b0100,
        GTEu        = 0b0101,
        LTu         = 0b0110,
        LTEu        = 0b0111,
        GTs         = 0b1000,
        GTEs        = 0b1001,
        LTs         = 0b1010,
        LTEs        = 0b1011,
        SF          = 0b1100,
        SX          = 0b1101,
        XX_0        = 0b1110,
        XX_1        = 0b1111,

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
                else if ((cw & ControlWord.MASK_BUSW) == ControlWord.IWORD)
                    sb.Append("IWORD ");
            }

            if ((cw & ControlWord.MASK_SEG) == ControlWord.CG)
                sb.Append("CG:");
            else if ((cw & ControlWord.MASK_SEG) == ControlWord.SG)
                sb.Append("SG:");
            else if ((cw & ControlWord.MASK_SEG) == ControlWord.PAG1)
                sb.Append("PAG1:");
            else if ((cw & ControlWord.MASK_SEG) == ControlWord.PAG2)
                sb.Append("PAG2:");
            else if ((cw & ControlWord.MASK_SEG) == ControlWord.PAG3)
                sb.Append("PAG3: ");
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
            }

            if ((cw & ControlWord.MASK_B) == ControlWord.bRSO1)
                sb.Append("RSO1 ");
            else if ((cw & ControlWord.MASK_B) == ControlWord.bRSO2)
                sb.Append("RSO2 ");
            else if ((cw & ControlWord.MASK_B) == ControlWord.bTBO)
                sb.Append("TBO ");
            else if ((cw & ControlWord.MASK_B) == ControlWord.bTCO)
                sb.Append("TCO ");
            else if ((cw & ControlWord.MASK_B) == ControlWord.FO)
                sb.Append("FO ");
            else if ((cw & ControlWord.MASK_B) == ControlWord.ISO1)
                sb.Append("ISO1 ");
            else if ((cw & ControlWord.MASK_B) == ControlWord.ISO2)
                sb.Append("ISO2 ");

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

            if ((cw & ControlWord.MASK_CONST) == ControlWord.Const1)
                sb.Append("1 ");
            else if ((cw & ControlWord.MASK_CONST) == ControlWord.Const2)
                sb.Append("2 ");
            else if ((cw & ControlWord.MASK_CONST) == ControlWord.Const3)
                sb.Append("3 ");
            else if ((cw & ControlWord.MASK_CONST) == ControlWord.Const4)
                sb.Append("4 ");
            else if ((cw & ControlWord.MASK_CONST) == ControlWord.CGI)
                sb.Append("CGI ");
            else if ((cw & ControlWord.MASK_CONST) == ControlWord.TGC)
                sb.Append("TGC ");
            else if ((cw & ControlWord.MASK_CONST) == ControlWord.PRVCHK)
                sb.Append("PRVCHK ");

            if ((cw & ControlWord.MASK_CMJ) == ControlWord.JNZ)
            {
                sb.Append("JNZ ");
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
            else if ((cw & ControlWord.MASK_IR) == ControlWord.FFI)
                sb.Append("FFI ");

            if ((cw & ControlWord.MASK_IPC) == ControlWord.IPC1)
                sb.Append("IPC1");
            else if ((cw & ControlWord.MASK_IPC) == ControlWord.IPC2)
                sb.Append("IPC2");
            else if ((cw & ControlWord.MASK_IPC) == ControlWord.IPC3)
                sb.Append("IPC3");
            else if ((cw & ControlWord.MASK_IPC) == ControlWord.IPC4)
                sb.Append("IPC4");
            else if ((cw & ControlWord.MASK_IPC) == ControlWord.IPC5)
                sb.Append("IPC5");
            else if ((cw & ControlWord.MASK_IPC) == ControlWord.IPC6)
                sb.Append("IPC6");
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
