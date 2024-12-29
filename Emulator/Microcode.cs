using System.Text;
using DiskUtil;

namespace Emulator
{
    public static class Microcode
    {
        public static MicrocodeRom AssembleRom(string microcodeFilePath)
        {
            var macros = new Dictionary<string, Procedure>();
            var opcodes = new Dictionary<int, Procedure>();
            var opcodesMetadata = new Dictionary<string, MicrocodeRom.Opcode>();
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

                        current = new Procedure(macroName, false);
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
                    int opcode = 0;

                    var operandType = Convert.ToInt32(parts[0], 2) & 0x03;
                    var opcodeIndex = Convert.ToInt32(parts[1], 2) & 0x1F;
                    var opcodeName = parts[2].ToUpperInvariant();
                    var operandCombination = 0;
                    var isWildcard = false;

                    opcodeIndex |= operandType << 4;
                    opcode |= opcodeIndex << 10;

                    if (operandType is 0b10 or 0b11) // 1 Operand
                    {
                        var operand1 = ParseOperand(parts[3], i);
                        var hasZF = parts.Contains("+ZF") ? 0x0040 : 0;
                        var hasCF = parts.Contains("+CF") ? 0x0020 : 0;
                        var hasSF = parts.Contains("+SF") ? 0x0010 : 0;
                        isWildcard = parts[4] == "*";
                        
                        opcode |= (int)operand1 << 7;
                        if (!isWildcard)
                        {
                            opcode |= hasZF;
                            opcode |= hasCF;
                            opcode |= hasSF;
                        }

                        operandCombination = ((byte)operand1 << 4);
                    }
                    else if (operandType == 0b01) // 2 Operand
                    {
                        int k = 3;
                        var operandOrder = 0; // AB
                        if (parts[3] == "BA")
                        {
                            operandOrder = 0x80;
                            ++k;
                        }
                        else if (parts[3] == "AB")
                            ++k;

                        var operand1 = ParseOperand(parts[k++], i);
                        var operand2 = ParseOperand(parts[k++], i);

                        opcode |= (int)operand1 << 7;
                        opcode |= (int)operand2 << 4;

                        operandCombination = (byte)operandOrder | ((byte)operand1 << 4) | (byte)operand2;
                    }
                    else if (operandType != 0)
                        throw new AssemblyException(i, $"Illegal operand count {operandType}");

                    current = new Procedure(opcodeName, isWildcard);
                    
                    if (!opcodesMetadata.TryGetValue(opcodeName, out var opcodeMetadata))
                    {
                        opcodeMetadata = new MicrocodeRom.Opcode
                        {
                            Name = opcodeName,
                            Index = opcodeIndex,
                            OperandCount = operandType switch
                            {
                                0b10 => 1,
                                0b11 => 1,
                                0b01 => 2,
                                0b00 => 0,
                                _ => 0
                            },
                            OperandCombinations = new List<byte>()
                        };
                        opcodesMetadata.Add(opcodeName, opcodeMetadata);
                    }

                    opcodeMetadata.OperandCombinations.Add((byte)operandCombination);

                    if (isWildcard)
                    {
                        for (int j = 0; j < 8; ++j)
                        {
                            var wcOpcode = opcode | (j << 4);
                            if (opcodes.TryGetValue(wcOpcode, out var existing) && !existing.IsWildcard)
                                throw new AssemblyException(i, $"Opcode '{opcodeName}' is already declared without wildcard");

                            opcodes[wcOpcode] = current;
                        }
                    }
                    else
                    {
                        if (opcodes.TryGetValue(opcode, out var existing) && !existing.IsWildcard)
                            throw new AssemblyException(i, $"Opcode '{opcodeName}' is already declared without wildcard");

                        opcodes[opcode] = current;
                    }
                }
                else
                {
                    var word = ControlWord.None;
                    foreach (var part in parts)
                    {
                        if (part == "END")
                        {
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

                            current.Labels.Add(labelName, current.CodeLength);
                            continue;
                        }
                        if (part[0] == '@')
                        {
                            var labelName = part[1..];
                            if (!current.Labels.TryGetValue(labelName, out var labelAddress))
                                throw new AssemblyException(i, $"Reference to undeclared label '{labelName}'");
                            if ((word & ControlWord.MASK_OPR) != 0)
                                throw new AssemblyException(i, $"Cannot reference label '{labelName}' here");

                            word |= (ControlWord)((labelAddress << 16) & (int)ControlWord.MASK_OPR);
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
                        throw new AssemblyException(l, $"Microcode exceeds {Procedure.MaxMicrocodeCount} words");
                    
                    current.Code[l] = word;
                }
            }

            var microcode = new ControlWord[MicrocodeRom.MaxControlWordCount];
            for (int addr = 0x0000; addr <= 0xFFF0; addr += 0x10)
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
                "BYTE[SEG:REG+sIMM]" => OperandType.DerefByteSegRegPlusSImm,
                "WORD[SEG:REG+sIMM]" => OperandType.DerefWordSegRegPlusSImm,
                "BYTE[SEG:REG]" => OperandType.DerefByteSegReg,
                "WORD[SEG:REG]" => OperandType.DerefWordSegReg,
                "[SEG:uIMM16]" => OperandType.DerefSegUImm16,
                _ => throw new AssemblyException(line, $"Illegal operand: {value}")
            };
        }

        private sealed class Procedure
        {
            public const int MaxMicrocodeCount = 16;

            public string Name { get; }

            public bool IsWildcard { get; }

            public ControlWord[] Code { get; }

            public int CodeLength { get; set; }

            public Dictionary<string, int> Labels { get; }

            public Procedure(string name, bool isWildcard)
            {
                Name = name;
                IsWildcard = isWildcard;
                Code = new ControlWord[MaxMicrocodeCount];
                CodeLength = 0;
                Labels = new Dictionary<string, int>();
            }
        }
    }

    public enum OperandType
    {
        Reg,
        Imm8,
        Imm16,
        DerefByteSegRegPlusSImm,
        DerefWordSegRegPlusSImm,
        DerefByteSegReg,
        DerefWordSegReg,
        DerefSegUImm16,

        None
    }

    public sealed class MicrocodeRom
    {
        public const int Magic = 0x52434D43;
        public const int CurrentFileVersion = 1;
        public const int MaxControlWordCount = 0xFFFF; // 16 microcode instructions per opcode
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
                for (int j = BytesPerControlWord - 1; j >= 0; --j)
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
                    writer.Write((byte)operandCombination);
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
                var operandCombinations = new List<byte>();
                for (int j = 0; j < combinationCount; ++j)
                    operandCombinations[j] = reader.ReadByte();

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

            public List<byte> OperandCombinations { get; init; }
        }
    }

    [Flags]
    public enum ControlWord : uint
    {
        None,

        IPC1        = 0x01,
        IPC2        = 0x02,
        IPC4        = 0x03,
        IPCOPRW1    = 0x04,
        IPCOPRW2    = 0x05,
        IPC_XX_1    = 0x06,
        IPC_XX_2    = 0x07,
        MASK_IPC    = 0x07,

        JMP         = 0x08,

        II          = 0x10,
        OI          = 0x20,
        FI          = 0x30,
        MASK_IR     = 0x30,
        
        RSO1        = 0x40,
        TAO         = 0x80,
        SPO         = 0xC0,
        MASK_A      = 0xC0,

        RSO2        = 0x100,
        TBO         = 0x200,
        FO          = 0x300,
        Const1      = 0x400,
        Const2      = 0x500,
        Const4      = 0x600,
        B_XX_2      = 0x700, // UNUSED
        MASK_B      = 0x700,
        
        RSI1        = 0x800,
        TAI         = 0x1000,
        TBI         = 0x1800,
        SPI         = 0x2000,
        JNF         = 0x2800,
        INTLATCH    = 0x3000,
        INTENLATCH  = 0x3800,
        MASK_RI     = 0x3800,
        
        R           = 0x4000,
        W           = 0x8000,

        BYTE        = 0,
        WORD        = 0x10000,
        OPRW1       = 0x20000,
        OPRW2       = 0x30000,
        MASK_BUSW   = 0x30000,

        ADD         = 0x40000,
        SUB         = 0x80000,
        OR          = 0xC0000,
        XOR         = 0x100000,
        AND         = 0x140000,
        SHL         = 0x180000,
        SHR         = 0x1C0000,
        MASK_ALU    = 0x1C0000,
        MASK_OPR    = 0x1C0000,
        
        DATA        = 0,
        ADDR        = 0x200000,

        CS          = 0x400000,
        SS          = 0x800000,
        DS          = 0xC00000,
        SEG1        = 0x1000000,
        SEG2        = 0x1400000,
        SEG_XX_1    = 0x1800000,
        SEG_XX_2    = 0x1C00000,
        MASK_SEG    = 0x1C00000,
        
        IPO         = 0x2000000,
        HLT         = 0x4000000,
        RTN         = 0x6000000,
        MASK_IP     = 0x6000000
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
                if ((cw & ControlWord.MASK_BUSW) == ControlWord.WORD)
                    sb.Append("WORD ");
                else if ((cw & ControlWord.MASK_BUSW) == ControlWord.OPRW1)
                    sb.Append("OPRW1 ");
                else if ((cw & ControlWord.MASK_BUSW) == ControlWord.OPRW2)
                    sb.Append("OPRW2 ");
                else
                    sb.Append("BYTE ");
            }

            if ((cw & ControlWord.MASK_SEG) == ControlWord.CS)
                sb.Append("CS:");
            else if ((cw & ControlWord.MASK_SEG) == ControlWord.SS)
                sb.Append("SS:");
            else if ((cw & ControlWord.MASK_SEG) == ControlWord.DS)
                sb.Append("DS:");
            else if ((cw & ControlWord.MASK_SEG) == ControlWord.SEG1)
                sb.Append("SEG1:");
            else if ((cw & ControlWord.MASK_SEG) == ControlWord.SEG2)
                sb.Append("SEG2:");

            if ((cw & ControlWord.ADDR) != 0)
                sb.Append("$");

            if ((cw & ControlWord.MASK_IP) == ControlWord.IPO)
                sb.Append("IPO ");
            if ((cw & ControlWord.MASK_IP) == ControlWord.HLT)
                sb.Append("HLT ");
            else if ((cw & ControlWord.MASK_IP) == ControlWord.RTN)
                sb.Append("RTN ");

            if ((cw & ControlWord.MASK_A) == ControlWord.RSO1)
                sb.Append("RSO1 ");
            else if ((cw & ControlWord.MASK_A) == ControlWord.TAO)
                sb.Append("TAO ");
            else if ((cw & ControlWord.MASK_A) == ControlWord.SPO)
                sb.Append("SPO ");

            if ((cw & ControlWord.MASK_RI) != ControlWord.JNF)
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
                else if ((cw & ControlWord.MASK_ALU) == ControlWord.SHL)
                    sb.Append("SHL ");
                else if ((cw & ControlWord.MASK_ALU) == ControlWord.SHR)
                    sb.Append("SHR ");
            }

            if ((cw & ControlWord.MASK_B) == ControlWord.RSO2)
                sb.Append("RSO2 ");
            else if ((cw & ControlWord.MASK_B) == ControlWord.TBO)
                sb.Append("TBO ");
            else if ((cw & ControlWord.MASK_B) == ControlWord.FO)
                sb.Append("FO ");
            else if ((cw & ControlWord.MASK_B) == ControlWord.Const1)
                sb.Append("1 ");
            else if ((cw & ControlWord.MASK_B) == ControlWord.Const2)
                sb.Append("2 ");
            else if ((cw & ControlWord.MASK_B) == ControlWord.Const4)
                sb.Append("4 ");

            if ((cw & ControlWord.MASK_RI) == ControlWord.RSI1)
                sb.Append("RSI1 ");
            else if ((cw & ControlWord.MASK_RI) == ControlWord.TAI)
                sb.Append("TAI ");
            else if ((cw & ControlWord.MASK_RI) == ControlWord.TBI)
                sb.Append("TBI ");
            else if ((cw & ControlWord.MASK_RI) == ControlWord.SPI)
                sb.Append("SPI ");
            else if ((cw & ControlWord.MASK_RI) == ControlWord.JNF)
            {
                sb.Append("JNF ");
                sb.Append((int)(cw & ControlWord.MASK_OPR) >> 16);
                sb.Append(' ');
            }
            else if ((cw & ControlWord.MASK_RI) == ControlWord.INTLATCH)
                sb.Append("INTLATCH ");
            else if ((cw & ControlWord.MASK_RI) == ControlWord.INTENLATCH)
                sb.Append("INTENLATCH ");

            if ((cw & ControlWord.MASK_IR) == ControlWord.II)
                sb.Append("II ");
            else if ((cw & ControlWord.MASK_IR) == ControlWord.OI)
                sb.Append("OI ");
            else if ((cw & ControlWord.MASK_IR) == ControlWord.FI)
                sb.Append("FI ");

            if ((cw & ControlWord.JMP) != 0)
                sb.Append("JMP ");

            if ((cw & ControlWord.MASK_IPC) == ControlWord.IPC1)
                sb.Append("IPC1");
            else if ((cw & ControlWord.MASK_IPC) == ControlWord.IPC2)
                sb.Append("IPC2");
            else if ((cw & ControlWord.MASK_IPC) == ControlWord.IPC4)
                sb.Append("IPC4");
            else if ((cw & ControlWord.MASK_IPC) == ControlWord.IPCOPRW1)
                sb.Append("IPCOPRW1");
            else if ((cw & ControlWord.MASK_IPC) == ControlWord.IPCOPRW2)
                sb.Append("IPCOPRW2");

            return sb.ToString();
        }
    }

    public sealed class AssemblyException : Exception
    {
        public int Line { get; }
            
        public AssemblyException(int line, string message)
            : base(message)
        {
            Line = line + 1;
        }
    }
}
