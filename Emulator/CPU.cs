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
        private const byte OF = 0x08;
        private const byte ZF = 0x04;
        private const byte CF = 0x02;
        private const byte SF = 0x01;

        public bool IsHalted { get; private set; }

        private int mci;
        private bool latchINT;
        private bool latchINTEN;

        private readonly Register r0, r1, r2, r3, r4, r5, r6, r7, sp, sg, cg, dg, tg;
        private readonly Register ta, tb, tc, lc, ip, flags, instruction, operand;
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
            instruction = new Register();
            operand = new Register();
            mci = 0;
        }

        // TODO fIMM3, f32
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

            ushort dbusWord = 0, dbusWordHi = 0;
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
            var isOF = (flags.Word & OF) == OF;
            var isZF = (flags.Word & ZF) == ZF;
            var isCF = (flags.Word & CF) == CF;
            var isSF = (flags.Word & SF) == SF;
            var ccIsAdv = cc switch
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
                _ => false
            };

            if (ccIsAdv)
            {
                ip.Word += 1;
                mci = 0;
                goto DoTick;
            }

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
                    reg = SelectRegister((operand.Word & 0xF000) >> 12);
                else if (acword == ControlWord.aRSO2)
                    reg = SelectRegister((operand.Word & 0x0F00) >> 8);
                else if (acword == ControlWord.aRSO3)
                    reg = SelectRegister((operand.Word & 0x00F0) >> 4);
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
                    reg = SelectRegister((operand.Word & 0x0F00) >> 8);
                else if (bcword == ControlWord.bRSO1)
                    reg = SelectRegister((operand.Word & 0xF000) >> 12);
                else if (bcword == ControlWord.bTBO)
                    reg = tb;
                else if (bcword == ControlWord.bTCO)
                    reg = tc;
                else if (bcword == ControlWord.FO)
                    reg = flags;
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

            if ((cword & ControlWord.Const1) == ControlWord.Const1)
            {
                if (isALUOperation)
                    alubWord |= 1;
                else
                    dbusWord |= 1;
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
                else if ((cword & ControlWord.MASK_ALU) == ControlWord.SHL)
                    alucWord = aluaWord << alubWord;
                else if ((cword & ControlWord.MASK_ALU) == ControlWord.SHR)
                    alucWord = aluaWord >> alubWord;

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
                    ControlWord.PAG1 => SelectPage((operand.Word & 0xF000) >> 12),
                    ControlWord.PAG2 => SelectPage((operand.Word & 0x0F00) >> 8),
                    _ => 0
                };

                if (isRead) // Read
                {
                    //if ((cword & ControlWord.MASK_BUSW) == ControlWord.ORW1)
                    //    dbusWord = SelectOperandWidth(instruction.Word) == 1
                    //             ? machine.ReadByte(seg, abusWord)
                    //             : machine.ReadWord(seg, abusWord);
                    //else if ((cword & ControlWord.MASK_BUSW) == ControlWord.ORW2)
                    //    dbusWord = SelectOperandWidth(operand.Word) == 1
                    //             ? machine.ReadByte(seg, abusWord)
                    //             : machine.ReadWord(seg, abusWord);
                    if ((cword & ControlWord.MASK_BUSW) == ControlWord.DWORD) // 32-bit
                    {
                        dbusWord  = machine.ReadWord(seg, abusWord);
                        dbusWordHi = machine.ReadWord(seg, (ushort)(abusWord + 2));
                    }
                    else if ((cword & ControlWord.MASK_BUSW) == ControlWord.WORD) // 16-bit
                        dbusWord = machine.ReadWord(seg, abusWord);
                    else
                        dbusWord = machine.ReadByte(seg, abusWord);
                }
                else if (isWrite) // Write
                {
                    //if ((cword & ControlWord.MASK_BUSW) == ControlWord.ORW1)
                    //{
                    //    if (SelectOperandWidth(instruction.Word) == 1)
                    //        machine.WriteByte(seg, abusWord, (byte)(dbusWord & 0xFF));
                    //    else
                    //        machine.WriteWord(seg, abusWord, dbusWord);
                    //}
                    //else if ((cword & ControlWord.MASK_BUSW) == ControlWord.ORW2)
                    //{
                    //    if (SelectOperandWidth(operand.Word) == 1)
                    //        machine.WriteByte(seg, abusWord, (byte)(dbusWord & 0xFF));
                    //    else
                    //        machine.WriteWord(seg, abusWord, dbusWord);
                    //}
                    if ((cword & ControlWord.MASK_BUSW) == ControlWord.DWORD) // 32-bit
                    {
                        machine.WriteWord(seg, abusWord, dbusWord);
                        machine.WriteWord(seg, (ushort)(abusWord + 2), dbusWord);
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
                operand.Word = (cword & ControlWord.MASK_BUSW) == ControlWord.DWORD ? dbusWordHi : operand.Word;
                cword = ResolveControlWord();
            }
            else if ((cword & ControlWord.MASK_IR) == ControlWord.FI)
            {
                if (isALUOperation)
                    flags.Word = (ushort)(zf | cf | sf);
                else
                    flags.Word = dbusWord;
            }
            else if ((cword & ControlWord.MASK_IR) == ControlWord.INTENLATCH)
            {
                latchINTEN = (dbusWord & 1) == 1;
            }

            var ricword = cword & ControlWord.MASK_RI;
            if (ricword != 0)
            {
                if (ricword == ControlWord.RSI1)
                {
                    var reg = SelectRegister((operand.Word & 0xF000) >> 12);
                    reg.Word = dbusWord;
                }
                else if (ricword == ControlWord.RSI2)
                {
                    var reg = SelectRegister((operand.Word & 0x0F00) >> 8);
                    reg.Word = dbusWord;
                }
                else if (ricword == ControlWord.RSI3)
                {
                    var reg = SelectRegister((operand.Word & 0x00F0) >> 4);
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
                if (cmjword == ControlWord.JNF && flags.Word == 0)
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

            if ((cword & ControlWord.TGC) == ControlWord.TGC)
                flags.Word ^= CF; 

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
            //else if ((cword & ControlWord.MASK_IPC) == ControlWord.IPCORW1)
            //    ip.Word += (ushort)SelectOperandWidth(instruction.Word);
            //else if ((cword & ControlWord.MASK_IPC) == ControlWord.IPCORW2)
            //    ip.Word += (ushort)SelectOperandWidth(operand.Word);
            else if ((cword & ControlWord.MASK_IPC) == ControlWord.JMP)
                ip.Word = dbusWord;
        }

        private ControlWord ResolveControlWord()
        {
            var iword = instruction.Word;
            var iaddr = (iword & 0x8000) switch
            {
                0x8000 => ((iword & 0xFC00) >> 1) | ((iword & 0x003F) << 3) | (mci & 0x07),
                0x0000 => ((iword & 0xFF00) >> 1) | (mci & 0x07),
                _ => throw new ArgumentOutOfRangeException(nameof(iword), iword, "Illegal Instruction Encoding")
            };

            return microcode[iaddr];
        }

        private Conditional ResolveConditional()
        {
            var iword = instruction.Word;
            return (iword & 0x8000) switch
            {
                0x8000 => (Conditional)(iword & 0x000F),
                0x0000 => (Conditional)((iword & 0x03C0) >> 6),
                _ => throw new ArgumentOutOfRangeException(nameof(iword), iword, "Illegal Instruction Encoding")
            };
        }

        private Register SelectRegister(int index)
        {
            return (index & 0x0F) switch
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
                _  => throw new ArgumentOutOfRangeException(nameof(index), index, "Illegal Register Index")
            };
        }

        private ushort SelectPage(int index)
        {
            return (index & 0x0F) switch
            {
                0  => dg.Word,
                1  => dg.Word,
                2  => dg.Word,
                3  => dg.Word,
                4  => dg.Word,
                5  => dg.Word,
                6  => cg.Word,
                7  => tg.Word,
                8  => sg.Word,
                9  => sg.Word,
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
