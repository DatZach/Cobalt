using System.Diagnostics;
using System.Text;

namespace Emulator
{
    public sealed class CPU
    {
        public bool IsHalted { get; private set; }

        private int mci;

        private readonly Register r0, r1, r2, r3, sp, ss, cs, ds, ta, tb, ip, flags, instruction, operand;
        private readonly Machine machine;
        private readonly ControlWord[] microcode;

        public CPU(Machine machine, MicrocodeRom microcodeRom)
        {
            this.machine = machine ?? throw new ArgumentNullException(nameof(machine));
            this.microcode = microcodeRom.Microcode;

            r0 = new Register();
            r1 = new Register();
            r2 = new Register();
            r3 = new Register();
            sp = new Register();
            ss = new Register();
            cs = new Register();
            ds = new Register();
            ta = new Register();
            tb = new Register();
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
            ushort dbusWord = 0;
            ushort abusWord = 0;
            ushort aluaWord = 0;
            ushort alubWord = 0;
            
            var cword = ResolveControlWord();

            if (machine.DebugOutput)
                Console.WriteLine($"{ip.Word:X4} {instruction.Word:X4} {(int)cword:X6} {cword.Disassemble()}");

            var isALUOperation = (cword & ControlWord.MASK_ALU) != 0;
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
            
            // Register Output
            if ((cword & ControlWord.RSO1) != 0)
            {
                var reg = SelectRegister(instruction.Word & 0x000F);
                if (isALUOperation)
                    aluaWord = reg.Word;
                else if (isAddr)
                    abusWord = reg.Word;
                else
                    dbusWord = reg.Word;
            }

            if ((cword & ControlWord.RSO2) != 0)
            {
                var reg = SelectRegister(operand.Word & 0x000F);
                if (isALUOperation)
                    alubWord = reg.Word;
                else
                    dbusWord = reg.Word;
            }

            if ((cword & ControlWord.TAO) != 0)
            {
                if (isALUOperation)
                    aluaWord = ta.Word;
                else if (isAddr)
                    abusWord = ta.Word;
                else
                    dbusWord = ta.Word;
            }

            if ((cword & ControlWord.TBO) != 0)
            {
                if (isALUOperation)
                    alubWord = tb.Word;
                else
                    dbusWord = tb.Word;
            }

            // ALU
            int zf = 0, cf = 0, sf = 0;
            if (isALUOperation)
            {
                int alucWord = 0;
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
                else if ((cword & ControlWord.MASK_ALU) == ControlWord.SHL)
                    alucWord = aluaWord << alubWord;
                else if ((cword & ControlWord.MASK_ALU) == ControlWord.SHR)
                    alucWord = aluaWord >> alubWord;

                zf = alucWord == 0 ? 0x04 : 0;
                cf = (alucWord & 0x10000) != 0 ? 0x02 : 0;
                sf = (alucWord & 0x8000) != 0 ? 0x01 : 0;
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
                    ControlWord.DS => ds.Word,
                    _ => 0
                };

                if (isRead) // Read
                {
                    if ((cword & ControlWord.WORD) != 0) // 16-bit
                        dbusWord = machine.RAM.ReadWord(seg, abusWord);
                    else
                        dbusWord = machine.RAM.ReadByte(seg, abusWord);
                }
                else if (isWrite) // Write
                {
                    if ((cword & ControlWord.WORD) != 0) // 16-bit
                        machine.RAM.WriteWord(seg, abusWord, dbusWord);
                    else
                        machine.RAM.WriteByte(seg, abusWord, (byte)(dbusWord & 0xFF));
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
                flags.Word = (ushort)(zf | cf | sf);

            if ((cword & ControlWord.MASK_RI) == ControlWord.RSI1)
            {
                var reg = SelectRegister(instruction.Word & 0x000F);
                reg.Word = dbusWord;
            }
            else if ((cword & ControlWord.MASK_RI) == ControlWord.TAI)
                ta.Word = dbusWord;
            else if ((cword & ControlWord.MASK_RI) == ControlWord.TBI)
                tb.Word = dbusWord;
            
            if ((cword & ControlWord.JMP) != 0)
                ip.Word = dbusWord;

            // CLOCK
            mci = (mci + 1) & 0x0F;

            // CLOCK FALLING EDGE
            if ((cword & ControlWord.MASK_IPC) == ControlWord.IPC1)
                ip.Word += 1;
            else if ((cword & ControlWord.MASK_IPC) == ControlWord.IPC2)
                ip.Word += 2;
            else if ((cword & ControlWord.MASK_IPC) == ControlWord.IPC4)
                ip.Word += 4;
        }

        private ControlWord ResolveControlWord()
        {
            var iword = instruction.Word;
            int iaddr;
            if ((iword & 0x8000) != 0)
                iaddr = (iword & 0xFF80) | ((flags.LoByte & 0x07) << 4);
            else if ((iword & 0x0300) != 0)
                iaddr = iword & 0xFFF0;
            else
                iaddr = iword & 0xFC00;
            iaddr |= mci;

            return microcode[iaddr];
        }

        private Register SelectRegister(int index)
        {
            return index switch
            {
                0 => r0,
                1 => r1,
                2 => r2,
                3 => r3,
                4 => sp,
                5 => ss,
                6 => cs,
                7 => ds,
                _ => throw new NotImplementedException()
            };
        }

        public CpuState CaptureState()
        {
            return new CpuState
            {
                r0 = r0, r1 = r1, r2 = r2, r3 = r3,
                sp = sp, ss = ss, cs = cs, ds = ds,
                ip = ip, flags = flags
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

        public override string ToString()
        {
            var sb = new StringBuilder();

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
            if (flags != null) sb.Append($"flags = {flags} ");

            return sb.ToString().TrimEnd();
        }
    }

    [DebuggerDisplay("{ToString()}")]
    public sealed class Register
    {
        public ushort Word { get; set; }

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
    }
}
