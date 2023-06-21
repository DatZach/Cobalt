using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Emulator
{
    internal sealed class CPU
    {
        public bool IsHalted { get; private set; }

        private int mci;

        public Register r0, r1, r2, r3, sp, ss, cs, ms, ta, tb, ip, instruction, operand;

        private readonly Machine machine;

        private readonly ControlWord[] microcode;

        public CPU(Machine machine)
        {
            this.machine = machine ?? throw new ArgumentNullException(nameof(machine));
            r0 = new Register();
            r1 = new Register();
            r2 = new Register();
            r3 = new Register();
            sp = new Register();
            ss = new Register();
            cs = new Register();
            ms = new Register();
            ta = new Register();
            tb = new Register();
            ip = new Register();
            instruction = new Register();
            operand = new Register();
            mci = 0;

            microcode = LoadMicrocode();
        }

        public void Tick()
        {
            if (IsHalted)
                return;

            ushort dbusWord = 0;
            ushort abusWord = 0;
            ushort aluaWord = 0;
            ushort alubWord = 0;

            var cword = microcode[(instruction.Word & 0xFFF0) | mci];
            mci = (mci + 1) & 0x0F;

            if ((cword & ControlWord.RTN) != 0)
            {
                mci = 0;
                return;
            }

            // IP Reg
            if ((cword & ControlWord.IPO) != 0)
            {
                abusWord = ip.Word;
            }

            if ((cword & ControlWord.IPC) != 0)
                ip.Word += 1;
            else if ((cword & ControlWord.IPC2) != 0)
                ip.Word += 2;

            if ((cword & ControlWord.JMP) != 0)
            {
                ip.Word = dbusWord;
            }

            // Register Output
            var isALUOperation = (cword & ControlWord.ADD) != 0;
            if ((cword & ControlWord.RSO1) != 0)
            {
                var reg = SelectRegister(instruction.Word & 0x000F);
                if (isALUOperation)
                    aluaWord = reg.Word;
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
                dbusWord = ta.Word;
            }

            // ALU
            if ((cword & ControlWord.ADD) != 0)
            {
                dbusWord = (ushort)(aluaWord + alubWord);
            }

            // RAM
            if ((cword & ControlWord.BE) != 0)
            {
                if ((cword & ControlWord.RW) != 0) // Read
                {
                    if ((cword & ControlWord.BW) != 0) // 16-bit
                        dbusWord = machine.RAM.ReadWord(ms.Word, abusWord);
                    else
                        dbusWord = machine.RAM.ReadByte(ms.Word, abusWord);
                }
                else // Write
                {
                    if ((cword & ControlWord.BW) != 0) // 16-bit
                        machine.RAM.WriteWord(ms.Word, abusWord, dbusWord);
                    else
                        machine.RAM.WriteByte(ms.Word, abusWord, (byte)(dbusWord & 0xFF));
                }
            }

            // Register Inputs
            if ((cword & ControlWord.II) != 0)
            {
                instruction.Word = dbusWord;
            }

            if ((cword & ControlWord.OI) != 0)
            {
                operand.Word = dbusWord;
            }

            if ((cword & ControlWord.RSI1) != 0)
            {
                var reg = SelectRegister(instruction.Word & 0x000F);
                reg.Word = dbusWord;
            }

            if ((cword & ControlWord.RSI2) != 0)
            {
                var reg = SelectRegister(operand.Word & 0x000F);
                reg.Word = dbusWord;
            }

            if ((cword & ControlWord.TAI) != 0)
            {
                ta.Word = dbusWord;
            }

            if ((cword & ControlWord.HLT) != 0)
            {
                IsHalted = true;
            }
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
                7 => ms,
                _ => throw new NotImplementedException()
            };
        }

        private static ControlWord[] LoadMicrocode()
        {
            var data = new ControlWord[0xFFFF];
            // NOP
            data[0x0000*16 + 0x0] = ParseControlWord("IPO BE BW RW II");
            data[0x0000*16 + 0x1] = ParseControlWord("IPC");
            data[0x0000*16 + 0x2] = ParseControlWord("RTN");

            // HLT
            data[0x0600 + 0x0] = ParseControlWord("IPO BE BW RW II");
            data[0x0600 + 0x1] = ParseControlWord("IPC");
            data[0x0600 + 0x2] = ParseControlWord("HLT");
            data[0x0600 + 0x3] = ParseControlWord("RTN");

            // MOV REG, IMM16
            data[0xB810 + 0x0] = ParseControlWord("IPO BE BW RW II");
            data[0xB810 + 0x1] = ParseControlWord("IPC2");
            data[0xB810 + 0x2] = ParseControlWord("RSI1 IPO BE BW RW");
            data[0xB810 + 0x3] = ParseControlWord("IPC2");
            data[0xB810 + 0x4] = ParseControlWord("RTN");

            // ADD REG, REG
            data[0xC000 + 0x0] = ParseControlWord("IPO BE BW RW II");
            data[0xC000 + 0x1] = ParseControlWord("IPC2");
            data[0xC000 + 0x2] = ParseControlWord("OI IPO BE RW");
            data[0xC000 + 0x3] = ParseControlWord("RSO1 RSO2 ADD TAI");
            data[0xC000 + 0x4] = ParseControlWord("RSI1 TAO IPC");
            data[0xC000 + 0x5] = ParseControlWord("RTN");

            return data;

            static ControlWord ParseControlWord(string value)
            {
                var result = ControlWord.None;
                foreach (var word in value.Split(' '))
                    result |= Enum.Parse<ControlWord>(word);

                return result;
            }
        }
    }

    [Flags]
    internal enum ControlWord : uint
    {
        None,
        JMP = 0x01,
        IPO = 0x02,
        IPC = 0x04,
        IPC2 = 0x08,
        BE = 0x10,
        RW = 0x20,
        BW = 0x40,
        RTN = 0x80,
        II = 0x100,
        RSI1 = 0x200,
        RSI2 = 0x400,
        RSO1 = 0x800,
        RSO2 = 0x1000,
        ADD = 0x2000,
        TAI = 0x4000,
        TAO = 0x8000,
        HLT = 0x10000,
        OI = 0x20000
    }

    internal class Register
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
    }
}
