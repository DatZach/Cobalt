using System.Diagnostics;
using System.Text;

namespace Emulator
{
    public sealed class CPU
    {
        private const byte OF = 0x08;
        private const byte ZF = 0x04;
        private const byte CF = 0x02;
        private const byte SF = 0x01;

        private const byte PM = 0x02;
        private const byte IE = 0x01;

        public bool IsHalted { get; private set; }

        private int mci;
        private bool latchINT;

        private readonly Register r0, r1, r2, r3, r4, r5, r6, r7, sp, sg, cg, dg, tg;
        private readonly Register ta, tb, tc, lc, ip, flags, features, instruction, operand0, operand1;
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
            r4 = new Register();
            r5 = new Register();
            r6 = new Register();
            r7 = new Register();
            sp = new Register();
            sg = new Register();
            cg = new Register { Word = 0x4000 };
            dg = new Register();
            tg = new Register();

            ta = new Register();
            tb = new Register();
            tc = new Register();
            lc = new Register();
            ip = new Register();
            flags = new Register();
            features = new Register();
            instruction = new Register();
            operand0 = new Register();
            operand1 = new Register();
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
            
            if (latchINT && (features.Word & IE) != 0)
            {
                instruction.Word = 0x0400;
                mci = 1;
            }

            ushort dbusWord = 0, dbusWordMid = 0, dbusWordHi = 0;
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
                    Console.Write($"\t\t{disassembler.Disassemble(cg.Word, ip.Word)}");
                Console.WriteLine();
            }

            var isALUOperation = (cword & ControlWord.MASK_ALU) != 0 && (cword & ControlWord.MASK_CMJ) == 0;
            var isAddr = (cword & ControlWord.ADDR) != 0;

            // CLOCK RISING EDGE
            var cc = ResolveConditional();
            if (mci == 1)
            {
                var isOF = (flags.Word & OF) == OF;
                var isZF = (flags.Word & ZF) == ZF;
                var isCF = (flags.Word & CF) == CF;
                var isSF = (flags.Word & SF) == SF;
                var isCond = cc switch
                {
                    Conditional.EQ   =>  isZF,
                    Conditional.NEQ  => !isZF,
                    Conditional.GTu  => !isZF && !isCF,
                    Conditional.GTEu => !isCF,
                    Conditional.LTu  =>  isCF,
                    Conditional.LTEu =>  isZF || isCF,
                    Conditional.GTs  => !isZF && isSF == isOF,
                    Conditional.GTEs =>  isSF == isOF,
                    Conditional.LTs  =>  isSF != isOF,
                    Conditional.LTEs =>  isZF || isSF != isOF,
                    _ => true
                };

                if (!isCond)
                {
                    ip.Word += 1;
                    mci = 0;
                    goto DoTick;
                }
            }

            // IP Reg
            if ((cword & ControlWord.MASK_IP) == ControlWord.IPO)
            {
                if (isALUOperation)
                    aluaWord = ip.Word;
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
                    reg = SelectRegister((operand0.Word & 0xF000) >> 12);
                else if (acword == ControlWord.aRSO2)
                    reg = SelectRegister((operand0.Word & 0x0F00) >> 8);
                else if (acword == ControlWord.aRSO3)
                    reg = SelectRegister((operand0.Word & 0x00F0) >> 4);
                else if (acword == ControlWord.TAO)
                    reg = ta;
                else if (acword == ControlWord.aTBO)
                    reg = tb;
                else if (acword == ControlWord.aTCO)
                    reg = tc;
                else if (acword == ControlWord.SPO)
                    reg = sp;
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
                    reg = SelectRegister(1);
                else if (bcword == ControlWord.bRSO1)
                    reg = SelectRegister(0);
                else if (bcword == ControlWord.bTBO)
                    reg = tb;
                else if (bcword == ControlWord.bTCO)
                    reg = tc;
                else if (bcword == ControlWord.FO)
                    reg = flags;
                else if (bcword == ControlWord.ISO1)
                    reg = SelectImmediate(0);
                else if (bcword == ControlWord.ISO2)
                    reg = SelectImmediate(1);
                else
                    throw new InvalidOperationException();

                if (isALUOperation)
                    alubWord = reg.Word;
                else
                    dbusWord = reg.Word;
            }

            var constword = cword & ControlWord.MASK_CONST;
            if (constword == ControlWord.CGI)
            {
                cg.Word = dbusWord;
            }
            else if (constword == ControlWord.TGC)
            {
                flags.Word ^= CF;
            }
            else if (constword == ControlWord.PRVCHK)
            {
                // TODO Protect 0-page
                if ((features.Word & PM) != 0)
                {
                    latchINT = true;
                    features.Word |= IE;
                    ta.Word = 4;
                    mci = 0;
                }
            }
            else if (constword != 0)
            {
                ushort data = constword switch
                {
                    ControlWord.Const1 => 1,
                    ControlWord.Const2 => 2,
                    ControlWord.Const3 => 3,
                    ControlWord.Const4 => 4,
                    _ => 0
                };

                if (isALUOperation)
                    alubWord = data;
                else
                    dbusWord = data;
            }

            // ALU
            int zf = 0, cf = 0, sf = 0;
            if (isALUOperation)
            {
                var fl = cc == Conditional.SF ? 0 : flags.Word;

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
                    alucWord = (aluaWord << alubWord) | ((fl & CF) == CF ? 0x0001 : 0);
                else if ((cword & ControlWord.MASK_ALU) == ControlWord.ROR)
                {
                    cfOverride = aluaWord & 1;
                    alucWord = (aluaWord >> alubWord) | ((fl & CF) == CF ? 0x8000 : 0);
                }

                // TODO Overflow Flag
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
                    ControlWord.CG => cg.Word,
                    ControlWord.SG => sg.Word,
                    ControlWord.PAG1 => SelectPage(0),
                    ControlWord.PAG2 => SelectPage(1),
                    ControlWord.PAG3 => SelectPage(2),
                    _ => 0
                };

                if (isRead) // Read
                {
                    if ((cword & ControlWord.MASK_BUSW) == ControlWord.IWORD) // 48-bit
                    {
                        dbusWord  = machine.ReadWord(seg, abusWord);
                        dbusWordMid = machine.ReadWord(seg, (ushort)(abusWord + 2));
                        dbusWordHi = machine.ReadWord(seg, (ushort)(abusWord + 4));
                    }
                    else if ((cword & ControlWord.MASK_BUSW) == ControlWord.WORD) // 16-bit
                        dbusWord = machine.ReadWord(seg, abusWord);
                    else
                        dbusWord = machine.ReadByte(seg, abusWord);
                }
                else if (isWrite) // Write
                {
                    if ((cword & ControlWord.MASK_BUSW) == ControlWord.IWORD) // 48-bit
                    {
                        machine.WriteWord(seg, abusWord, dbusWord);
                        machine.WriteWord(seg, (ushort)(abusWord + 2), dbusWord);
                        machine.WriteWord(seg, (ushort)(abusWord + 4), dbusWord);
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
                if ((cword & ControlWord.MASK_BUSW) == ControlWord.IWORD)
                {
                    operand0.Word = dbusWordMid;
                    operand1.Word = dbusWordHi;
                }

                cword = ResolveControlWord();
            }
            else if ((cword & ControlWord.MASK_IR) == ControlWord.FI && cc != Conditional.SF)
            {
                if (isALUOperation)
                    flags.Word = (ushort)(zf | cf | sf);
                else
                    flags.Word = dbusWord;
            }
            else if ((cword & ControlWord.MASK_IR) == ControlWord.FFI)
                features.Word = dbusWord;

            var ricword = cword & ControlWord.MASK_RI;
            if (ricword != 0)
            {
                if (ricword == ControlWord.RSI1)
                {
                    var reg = SelectRegister(0);
                    reg.Word = dbusWord;
                }
                else if (ricword == ControlWord.RSI2)
                {
                    var reg = SelectRegister(1);
                    reg.Word = dbusWord;
                }
                else if (ricword == ControlWord.RSI3)
                {
                    var reg = SelectRegister(2);
                    reg.Word = dbusWord;
                }
                else if (ricword == ControlWord.TAI)
                    ta.Word = dbusWord;
                else if (ricword == ControlWord.TBI)
                    tb.Word = dbusWord;
                else if (ricword == ControlWord.TCI)
                    tc.Word = dbusWord;
                else if (ricword == ControlWord.SPI)
                    sp.Word = dbusWord;
                else
                    throw new InvalidOperationException();
            }

            var cmjword = cword & ControlWord.MASK_CMJ;
            if (cmjword != 0)
            {
                var mciAddr = (int)(cword & ControlWord.MASK_OPR) >> 18;
                if (cmjword == ControlWord.JNZ && (flags.Word & ZF) != ZF)
                    mci = mciAddr;
                else if (cmjword == ControlWord.JC && (flags.Word & CF) == CF)
                    mci = mciAddr;
                else if (cmjword == ControlWord.LNZ)
                {
                    lc.Word = (ushort)((lc.Word + 1) & 0x0F);
                    if (lc.Word != 0)
                        mci = mciAddr;
                }
            }

            if ((cword & ControlWord.MASK_SEG) == ControlWord.LI16)
                lc.Word = 0;
            if ((cword & ControlWord.MASK_SEG) == ControlWord.INTLATCH)
                latchINT = (dbusWord & 1) == 1;

            // CLOCK
            mci = (mci + 1) & 0x07;

            // CLOCK FALLING EDGE
            if ((cword & ControlWord.MASK_IPC) == ControlWord.IPC1)
                ip.Word += 1;
            else if ((cword & ControlWord.MASK_IPC) == ControlWord.IPC2)
                ip.Word += 2;
            else if ((cword & ControlWord.MASK_IPC) == ControlWord.IPC3)
                ip.Word += 3;
            else if ((cword & ControlWord.MASK_IPC) == ControlWord.IPC4)
                ip.Word += 4;
            else if ((cword & ControlWord.MASK_IPC) == ControlWord.IPC5)
                ip.Word += 5;
            else if ((cword & ControlWord.MASK_IPC) == ControlWord.IPC6)
                ip.Word += 6;
            else if ((cword & ControlWord.MASK_IPC) == ControlWord.JMP)
                ip.Word = dbusWord;
        }

        private ControlWord ResolveControlWord()
        {
            var iword = instruction.Word;
            var iaddr = (iword & 0x8F00) switch
            {
                0x0000 => (iword & 0x7000)      | (iword & 0x00C0) << 4 | 
                          (iword & 0x8000) >> 6 | (iword & 0x001F) << 4 | (mci & 0x07),
                _      => (iword & 0x7000) >> 8 | (mci & 0x07) | 0x7F80
            };

            return microcode[iaddr];
        }

        private Conditional ResolveConditional()
        {
            var iword = instruction.Word;
            return (iword & 0x8000) switch
            {
                0x0000 => (Conditional)((iword & 0x0F00) >> 8),
                _      => Conditional.None
            };
        }

        private Register SelectRegister(int index)
        {
            var value = ResolveRegisterIndex(index);
            return (value & 0x0F) switch
            {
                0  => r0,
                1  => r1,
                2  => r2,
                3  => r3,
                4  => r4,
                5  => r5,
                6  => r6,
                7  => r7,
                8  => sp,
                9  => sg,
                10 => cg,
                11 => dg,
                12 => tg,
                13 => r0.Lo,
                14 => r0.Hi,
                15 => r1.Lo,
                _  => throw new ArgumentOutOfRangeException(nameof(value), value, "Illegal Register Value Index")
            };
        }

        private Register SelectImmediate(int index)
        {
            var format = instruction.Word & 0x07;
            var value = (operand0.Word << 16) | operand1.Word;
            var imm = index switch
            {
                0 => format switch
                {
                    0 => (value & 0xFF000000) >> 24,
                    1 => (value & 0xFFFF0000) >> 16,
                    2 => (value & 0x0FFFF000) >> 12,
                    3 => (value & 0x00FF0000) >> 16,
                    4 => (value & 0x00FFFF00) >> 8,
                    5 => (value & 0x00FF0000) >> 16,
                    6 => (value & 0x00FFF000) >> 12,
                    7 => (value & 0x0F000000) >> 24,
                    _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Illegal Immediate Format")
                },
                1 => format switch
                {
                    0 => (value & 0x00FF0000) >> 16,
                    1 => (value & 0x0000FFFF) >> 0,
                    2 => (value & 0x00000FFF) >> 0,
                    3 => (value & 0x0000FFFF) >> 0,
                    4 => (value & 0x0000FFFF) >> 0,
                    5 => (value & 0x0000FF00) >> 8,
                    6 => (value & 0x00000FFF) >> 0,
                    7 => (value & 0x00FF0000) >> 16,
                    _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Illegal Immediate Format")
                },
                _ => throw new ArgumentOutOfRangeException(nameof(index), index, "Illegal Immediate Index")
            };

            return new Register { Word = (ushort)imm };
        }

        private ushort SelectPage(int index)
        {
            var value = ResolveRegisterIndex(index);
            return (value & 0x0F) switch
            {
                0  => dg.Word,
                1  => dg.Word,
                2  => dg.Word,
                3  => dg.Word,
                4  => dg.Word,
                5  => sg.Word,
                6  => tg.Word,
                7  => cg.Word,
                8  => sg.Word,
                9  => sg.Word,
                10 => 0xE000,
                11 => 0xC000,
                12 => 0x8000,
                13 => 0x4000,
                14 => 0x2000,
                15 => 0x0000,
                _  => throw new ArgumentOutOfRangeException(nameof(value), value, "Illegal Segment Value Index")
            };
        }

        private int ResolveRegisterIndex(int index)
        {
            if ((instruction.Word & 0x8000) == 0) // has flags
            {
                return index switch
                {
                    0 => (operand0.Word & 0xF000) >> 12,
                    1 => (operand0.Word & 0x0F00) >> 8,
                    2 => (operand0.Word & 0x00F0) >> 4,
                    _ => throw new ArgumentOutOfRangeException(nameof(index), index, "Illegal Register Index")
                };
            }

            return index switch
            {
                0 => (instruction.Word & 0x0F00) >> 8,
                1 => (operand0.Word & 0xF000) >> 12,
                2 => (operand0.Word & 0x0F00) >> 8,
                _ => throw new ArgumentOutOfRangeException(nameof(index), index, "Illegal Register Index")
            };
        }

        public CpuState CaptureState()
        {
            return new CpuState
            {
                r0 = r0, r1 = r1, r2 = r2, r3 = r3, r4 = r4, r5 = r5, r6 = r6, r7 = r7,
                sp = sp, sg = sg, cg = cg, dg = dg, tg = tg,
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

        public Register? r4 { get; init; }

        public Register? r5 { get; init; }

        public Register? r6 { get; init; }

        public Register? r7 { get; init; }

        public Register? sp { get; init; }

        public Register? sg { get; init; }

        public Register? cg { get; init; }

        public Register? dg { get; init; }

        public Register? tg { get; init; }

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
            if (r3 != null) sb.Append($"r4 = {r4} ");
            if (r3 != null) sb.Append($"r5 = {r5} ");
            if (r3 != null) sb.Append($"r6 = {r6} ");
            if (r3 != null) sb.Append($"r7 = {r7} ");
            sb.AppendLine();

            if (sp != null) sb.Append($"sp = {sp} ");
            if (sg != null) sb.Append($"sg = {sg} ");
            if (cg != null) sb.Append($"cg = {cg} ");
            if (dg != null) sb.Append($"dg = {dg} ");
            if (dg != null) sb.Append($"tg = {tg} ");
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
