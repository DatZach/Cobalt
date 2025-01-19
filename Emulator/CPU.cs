using System.Diagnostics;
using System.Text;

namespace Emulator
{
    public sealed class CPU
    {
        private static readonly Register Constant0 = new() { Word = 0 };
        private static readonly Register Constant1 = new() { Word = 1 };
        private static readonly Register Constant2 = new() { Word = 2 };
        private static readonly Register Constant4 = new() { Word = 4 };
        private const ushort iINT_Hi = 0x0C;
        private const byte ZF = 0x04;
        private const byte CF = 0x02;
        private const byte SF = 0x01;

        public bool IsHalted { get; private set; }

        private int mci;
        private bool latchINT;
        private bool latchINTEN;

        private readonly Register r0, r1, r2, r3, sp, ss, cs, ds, ta, tb, tc, ip, flags, instruction, operand;
        private readonly Machine machine;
        private readonly ControlWord[] microcode;
        private readonly Disassembler disassembler;

        public CPU(Machine machine, MicrocodeRom microcodeRom)
        {
            this.machine = machine ?? throw new ArgumentNullException(nameof(machine));
            this.microcode = microcodeRom.Microcode;
            this.disassembler = new Disassembler(microcodeRom, machine);
            
            r0 = new Register();
            r1 = new Register();
            r2 = new Register();
            r3 = new Register();
            sp = new Register();
            ss = new Register();
            cs = new Register { Word = 0x4000 };
            ds = new Register();
            ta = new Register();
            tb = new Register();
            tc = new Register();
            ip = new Register();
            flags = new Register();
            instruction = new Register();
            operand = new Register();
            mci = 0;
        }

        public void Tick()
        {
            if (IsHalted)
                return;

            DoTick:
            if (machine.IsInterruptAsserted && mci == 0)
            {
                latchINT = true;
                ta.Word = 0;
            }
            
            if (latchINT && latchINTEN)
            {
                instruction.Word = 0x0400;
                mci = 1;
            }

            ushort dbusWord = 0;
            ushort abusWord = 0;
            ushort aluaWord = 0;
            ushort alubWord = 0;

            var cword = ResolveControlWord();

            if (cword == ControlWord.None)
                throw new Exception("Illegal control word encountered!");

            if (machine.DebugOutput)
            {
                Console.Write($"{ip.Word:X4} {instruction.Word:X4} {(int)cword:X8} {cword.Disassemble()}");
                if (mci == 0)
                    Console.Write($"\t\t{disassembler.Disassemble(cs.Word, ip.Word)}");
                Console.WriteLine();
            }

            var isALUOperation = (cword & ControlWord.MASK_ALU) != 0 && (cword & ControlWord.MASK_CMJ) == 0;
            var isAddr = (cword & ControlWord.ADDR) != 0;

            // CLOCK RISING EDGE
            // IP Reg
            if ((cword & ControlWord.MASK_IP) == ControlWord.IPO)
            {
                if (isALUOperation)
                    throw new InvalidOperationException();
                else if (isAddr)
                    abusWord = ip.Word;
                else
                    dbusWord = ip.Word;
            }
            else if ((cword & ControlWord.MASK_IP) == ControlWord.HLT)
                IsHalted = true;
            else if ((cword & ControlWord.MASK_IP) == ControlWord.RTN)
            {
                // NOTE RTN Doesn't "tick", it immediately resets the MCI and 0th
                //      control word executes again
                mci = 0;
                goto DoTick;
            }
            
            // A-Line -- Register Output
            var acword = cword & ControlWord.MASK_A;
            if (acword != 0)
            {
                Register? reg;
                if (acword == ControlWord.aRSO1)
                    reg = SelectRegister(instruction.Word, (instruction.Word & 0x380) >> 7);
                else if (acword == ControlWord.aRSO2)
                    reg = SelectRegister(operand.Word, (instruction.Word & 0x70) >> 4);
                else if (acword == ControlWord.TAO)
                    reg = ta;
                else if (acword == ControlWord.aTBO)
                    reg = tb;
                else if (acword == ControlWord.TCO)
                    reg = tc;
                else if (acword == ControlWord.SPO)
                    reg = sp;
                else if (acword == ControlWord.INTENLATCH)
                    reg = ip; // HACK Hardware does not assert anything on the a-line
                else
                    throw new InvalidOperationException();

                if (isALUOperation)
                    aluaWord = reg.Word;
                else if (isAddr)
                    abusWord = reg.Word;
                else
                    dbusWord = reg.Word;
            }

            // B-Line -- Register Output
            var bcword = cword & ControlWord.MASK_B;
            if (bcword != 0)
            {
                Register? reg;
                if (bcword == ControlWord.bRSO2)
                    reg = SelectRegister(operand.Word, (instruction.Word & 0x70) >> 4);
                else if (bcword == ControlWord.bRSO1)
                    reg = SelectRegister(instruction.Word, (instruction.Word & 0x380) >> 7);
                else if (bcword == ControlWord.bTBO)
                    reg = tb;
                else if (bcword == ControlWord.FO)
                    reg = flags;
                else if (bcword == ControlWord.Const1)
                    reg = Constant1;
                else if (bcword == ControlWord.Const2)
                    reg = Constant2;
                else if (bcword == ControlWord.Const4)
                    reg = Constant4;
                else
                    throw new InvalidOperationException();

                if (isALUOperation)
                    alubWord = reg.Word;
                else
                    dbusWord = reg.Word;
            }
            
            // ALU
            int zf = 0, cf = 0, sf = 0;
            if (isALUOperation)
            {
                int alucWord = 0, cfOverride = 0;
                if ((cword & ControlWord.MASK_ALU) == ControlWord.ADD)
                    alucWord = aluaWord + alubWord;
                else if ((cword & ControlWord.MASK_ALU) == ControlWord.SUB)
                    alucWord = aluaWord - alubWord;
                else if ((cword & ControlWord.MASK_ALU) == ControlWord.OR)
                    alucWord = aluaWord | alubWord;
                else if ((cword & ControlWord.MASK_ALU) == ControlWord.XOR)
                    alucWord = aluaWord ^ alubWord;
                else if ((cword & ControlWord.MASK_ALU) == ControlWord.AND)
                    alucWord = aluaWord & alubWord;
                else if ((cword & ControlWord.MASK_ALU) == ControlWord.ROL)
                    alucWord = (aluaWord << alubWord) | ((flags.Word & CF) == CF ? 0x0001 : 0);
                else if ((cword & ControlWord.MASK_ALU) == ControlWord.ROR)
                {
                    cfOverride = aluaWord & 1;
                    alucWord = (aluaWord >> alubWord) | ((flags.Word & CF) == CF ? 0x8000 : 0);
                }

                zf = alucWord == 0 ? ZF : 0;
                cf = (alucWord & 0x10000) != 0 || cfOverride != 0 ? CF : 0;
                sf = (alucWord & 0x8000) != 0 ? SF : 0;
                dbusWord = (ushort)alucWord;
            }

            // RAM
            var isRead = (cword & ControlWord.R) != 0;
            var isWrite = (cword & ControlWord.W) != 0;
            if (isRead || isWrite)
            {
                ushort seg = (cword & ControlWord.MASK_SEG) switch
                {
                    ControlWord.CS => cs.Word,
                    ControlWord.SS => ss.Word,
                    ControlWord.SEG1 => SelectSegment(instruction.Word),
                    ControlWord.SEG2 => SelectSegment(operand.Word),
                    _ => 0
                };

                if (isRead) // Read
                {
                    if ((cword & ControlWord.MASK_BUSW) == ControlWord.ORW1)
                        dbusWord = SelectOperandWidth(instruction.Word) == 1
                                 ? machine.ReadByte(seg, abusWord)
                                 : machine.ReadWord(seg, abusWord);
                    else if ((cword & ControlWord.MASK_BUSW) == ControlWord.ORW2)
                        dbusWord = SelectOperandWidth(operand.Word) == 1
                                 ? machine.ReadByte(seg, abusWord)
                                 : machine.ReadWord(seg, abusWord);
                    else if ((cword & ControlWord.MASK_BUSW) == ControlWord.WORD) // 16-bit
                        dbusWord = machine.ReadWord(seg, abusWord);
                    else
                        dbusWord = machine.ReadByte(seg, abusWord);
                }
                else if (isWrite) // Write
                {
                    if ((cword & ControlWord.MASK_BUSW) == ControlWord.ORW1)
                    {
                        if (SelectOperandWidth(instruction.Word) == 1)
                            machine.WriteByte(seg, abusWord, (byte)(dbusWord & 0xFF));
                        else
                            machine.WriteWord(seg, abusWord, dbusWord);
                    }
                    else if ((cword & ControlWord.MASK_BUSW) == ControlWord.ORW2)
                    {
                        if (SelectOperandWidth(operand.Word) == 1)
                            machine.WriteByte(seg, abusWord, (byte)(dbusWord & 0xFF));
                        else
                            machine.WriteWord(seg, abusWord, dbusWord);
                    }
                    else if ((cword & ControlWord.MASK_BUSW) == ControlWord.WORD) // 16-bit
                        machine.WriteWord(seg, abusWord, dbusWord);
                    else
                        machine.WriteByte(seg, abusWord, (byte)(dbusWord & 0xFF));
                }
            }

            // Register Inputs
            if ((cword & ControlWord.MASK_IR) == ControlWord.II)
            {
                // NOTE Not entirely sure if the cword immediately re-resolving after the IR
                //      is updated is accurate. Emulation of a single cycle fetch requires
                //      that the cword be accurate for the IPC* to execute on the falling edge
                //      correctly however... needs verification in real hardware
                instruction.Word = dbusWord;
                cword = ResolveControlWord();
            }
            else if ((cword & ControlWord.MASK_IR) == ControlWord.OI)
                operand.Word = dbusWord;
            else if ((cword & ControlWord.MASK_IR) == ControlWord.FI)
            {
                if (isALUOperation)
                    flags.Word = (ushort)(zf | cf | sf);
                else
                    flags.Word = dbusWord;
            }

            var ricword = cword & ControlWord.MASK_RI;
            if (ricword != 0)
            {
                if (ricword == ControlWord.RSI1)
                {
                    var reg = SelectRegister(instruction.Word, (instruction.Word & 0x380) >> 7);
                    reg.Word = dbusWord;
                }
                else if (ricword == ControlWord.RSI2)
                {
                    var reg = SelectRegister(operand.Word, (instruction.Word & 0x70) >> 4);
                    reg.Word = dbusWord;
                }
                else if (ricword == ControlWord.TAI)
                    ta.Word = dbusWord;
                else if (ricword == ControlWord.TBI)
                    tb.Word = dbusWord;
                else if (ricword == ControlWord.SPI)
                    sp.Word = dbusWord;
                else
                    throw new InvalidOperationException();
            }

            var cmjword = cword & ControlWord.MASK_CMJ;
            if (cmjword != 0)
            {
                var mciAddr = (int)(cword & ControlWord.MASK_OPR) >> 18;
                if (cmjword == ControlWord.JNF && flags.Word == 0)
                    mci = mciAddr;
                else if (cmjword == ControlWord.JC && (flags.Word & CF) == CF)
                    mci = mciAddr;
                else if (cmjword == ControlWord.LNZ)
                {
                    ++tc.Word;
                    if ((tc.Word & 0x10) != 0x10)
                        mci = mciAddr;
                }
            }

            if ((cword & ControlWord.TCI) == ControlWord.TCI)
                tc.Word = dbusWord;
            if ((cword & ControlWord.MASK_SEG) == ControlWord.INTLATCH)
                latchINT = (dbusWord & 1) == 1;
            if ((cword & ControlWord.MASK_A) == ControlWord.INTENLATCH)
                latchINTEN = (dbusWord & 1) == 1;

            if ((cword & ControlWord.TGC) == ControlWord.TGC)
                flags.Word ^= CF; 

            // CLOCK
            mci = (mci + 1) & 0x0F;

            // CLOCK FALLING EDGE
            if ((cword & ControlWord.MASK_IPC) == ControlWord.IPC1)
                ip.Word += 1;
            else if ((cword & ControlWord.MASK_IPC) == ControlWord.IPC2)
                ip.Word += 2;
            else if ((cword & ControlWord.MASK_IPC) == ControlWord.IPC4)
                ip.Word += 4;
            else if ((cword & ControlWord.MASK_IPC) == ControlWord.IPCORW1)
                ip.Word += (ushort)SelectOperandWidth(instruction.Word);
            else if ((cword & ControlWord.MASK_IPC) == ControlWord.IPCORW2)
                ip.Word += (ushort)SelectOperandWidth(operand.Word);
            else if ((cword & ControlWord.MASK_IPC) == ControlWord.JMP)
                ip.Word = dbusWord;
        }

        private ControlWord ResolveControlWord()
        {
            var iword = instruction.Word;
            var iaddr = (iword & 0xC000) switch
            {
                0xC000 or 0x8000 => (iword & 0xFF80) | ((flags.LoByte & 0x07) << 4) | mci,
                0x4000           => (iword & 0xFFF0) | mci,
                0x0000           => (iword & 0xFC00) | mci,
                _ => throw new ArgumentOutOfRangeException(nameof(iword), iword, "Illegal Instruction Encoding")
            };

            return microcode[iaddr];
        }

        private Register SelectRegister(int index, int operandType)
        {
            return (operandType & 0x07) switch
            {
                0b000 => (index & 0x0F) switch
                {
                    0  => r0,
                    1  => r1,
                    2  => r2,
                    3  => r3,
                    4  => sp,
                    5  => ss,
                    6  => cs,
                    7  => ds,
                    8  => r0.Hi,
                    9  => r0.Lo,
                    10 => r1.Hi,
                    11 => r1.Lo,
                    12 => r2.Hi,
                    13 => r2.Lo,
                    14 => r3.Hi,
                    15 => r3.Lo,
                    _  => throw new ArgumentOutOfRangeException(nameof(index), index, "Illegal Register Index")
                },
                0b001 or
                0b010 => throw new ArgumentOutOfRangeException(nameof(operandType), operandType, "Illegal Operand Type"),
                0b011 or
                0b100 or
                0b101 or
                0b110 or
                0b111 => (index & 0x0F) switch
                {
                    0 => r0,
                    1 => r1,
                    2 => r2,
                    3 => r3,
                    4 => sp,
                    5 => r1,
                    6 => r2,
                    7 => r3,
                    8 => r0,
                    9 => r0,
                    10 => r1,
                    11 => r1,
                    12 => r2,
                    13 => r2,
                    14 => r3,
                    15 => r3,
                    _  => throw new ArgumentOutOfRangeException(nameof(index), index, "Illegal Register Index")
                }

            };
        }

        private ushort SelectSegment(int index)
        {
            return (index & 0x0F) switch
            {
                0  => ds.Word,
                1  => ds.Word,
                2  => ds.Word,
                3  => ds.Word,
                4  => ss.Word,
                5  => ss.Word,
                6  => cs.Word,
                7  => ds.Word,
                8  => ss.Word,
                9  => cs.Word,
                10 => 0xE000,
                11 => 0xC000,
                12 => 0x8000,
                13 => 0x4000,
                14 => 0x2000,
                15 => 0x0000,
                _  => throw new ArgumentOutOfRangeException(nameof(index), index, "Illegal Segment Index")
            };
        }

        private static int SelectOperandWidth(int index) => (index & 0xC) == 0x4 ? 1 : 2;

        public CpuState CaptureState()
        {
            return new CpuState
            {
                r0 = r0, r1 = r1, r2 = r2, r3 = r3,
                sp = sp, ss = ss, cs = cs, ds = ds,
                ip = ip, flags = flags,
                ta = ta, tb = tb, tc = tc
            };
        }
    }

    public sealed record CpuState
    {
        public Register? r0 { get; init; }

        public Register? r1 { get; init; }

        public Register? r2 { get; init; }

        public Register? r3 { get; init; }

        public Register? sp { get; init; }

        public Register? ss { get; init; }

        public Register? cs { get; init; }

        public Register? ds { get; init; }

        public Register? ip { get; init; }

        public Register? flags { get; init; }

        public Register? ta { get; init; }

        public Register? tb { get; init; }

        public Register? tc { get; init; }

        public override string ToString()
        {
            var sb = new StringBuilder(128);

            if (r0 != null) sb.Append($"r0 = {r0} ");
            if (r1 != null) sb.Append($"r1 = {r1} ");
            if (r2 != null) sb.Append($"r2 = {r2} ");
            if (r3 != null) sb.Append($"r3 = {r3} ");
            sb.AppendLine();

            if (sp != null) sb.Append($"sp = {sp} ");
            if (ss != null) sb.Append($"ss = {ss} ");
            if (cs != null) sb.Append($"cs = {cs} ");
            if (ds != null) sb.Append($"ds = {ds} ");
            sb.AppendLine();

            if (ip != null) sb.Append($"ip = {ip} ");
            if (flags != null)
            {
                sb.Append($"flags = {flags} ");
                sb.Append((flags.Word & 0x0004) == 0 ? "zf " : "ZF ");
                sb.Append((flags.Word & 0x0002) == 0 ? "cf " : "CF ");
                sb.Append((flags.Word & 0x0001) == 0 ? "sf " : "SF ");
            }
            sb.AppendLine();

            if (ta != null) sb.Append($"ta = {ta} ");
            if (tb != null) sb.Append($"tb = {tb} ");
            if (tc != null) sb.Append($"tc = {tc} ");

            return sb.ToString().TrimEnd();
        }
    }

    [DebuggerDisplay("{ToString()}")]
    public class Register
    {
        public virtual ushort Word { get; set; }

        public byte LoByte
        {
            get => (byte)(Word & 0xFF);
            set => Word = (ushort)((HiByte << 8) | (value & 0xFF));
        }

        public byte HiByte
        {
            get => (byte)((Word >> 8) & 0xFF);
            set => Word = (ushort)((value << 8) | LoByte);
        }

        private RegisterFragment? loFragment;
        public RegisterFragment Lo => loFragment ??= new RegisterFragment(this, 0);

        private RegisterFragment? hiFragment;
        public RegisterFragment Hi => hiFragment ??= new RegisterFragment(this, 8);

        public static implicit operator Register(int value)
        {
            return new Register { Word = (ushort)value };
        }

        public static bool operator ==(Register? a, Register? b)
        {
            if (ReferenceEquals(a, null) && ReferenceEquals(b,null)) return true;
            if (ReferenceEquals(a, null)) return false;
            if (ReferenceEquals(b, null)) return false;

            return a.Word == b.Word;
        }

        public static bool operator !=(Register? a, Register? b)
        {
            return !(a == b);
        }

        public override string ToString()
        {
            return Word.ToString("X4");
        }

        public sealed class RegisterFragment : Register
        {
            public override ushort Word
            {
                get => (byte)((register.Word >> shift) & 0xFF);
                set => register.Word = (ushort)((register.Word & (0xFF00 >> shift)) | ((value & 0xFF) << shift));
            }

            private readonly Register register;
            private readonly int shift;

            public RegisterFragment(Register register, int shift)
            {
                this.register = register ?? throw new ArgumentNullException(nameof(register));
                this.shift = shift;
            }
        }
    }
}
