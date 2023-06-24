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

        public Register r0, r1, r2, r3, sp, ss, cs, ms, ta, tb, ip, flags, instruction, operand;

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
            flags = new Register();
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

            var iword = instruction.Word;
            int iaddr;
            if ((iword & 0x8000) != 0)
                iaddr = (iword & 0xFF80) | ((flags.LoByte & 0x07) << 4);
            else if ((iword & 0x0300) != 0)
                iaddr = iword & 0xFFF0;
            else
                iaddr = (iword & 0xFC00) >> 8;
            iaddr |= mci;

            var cword = microcode[iaddr];
            mci = (mci + 1) & 0x0F;

            if ((cword & ControlWord.RTN) != 0)
            {
                mci = 0;
                return;
            }

            // IP Reg
            //if ((cword & ControlWord.IPO) != 0)
            //{
            //    abusWord = ip.Word;
            //}

            //if ((cword & ControlWord.IPC) != 0)
            //    ip.Word += 1;
            //else if ((cword & ControlWord.IPC2) != 0)
            //    ip.Word += 2;

            //if ((cword & ControlWord.JMP) != 0)
            //{
            //    ip.Word = dbusWord;
            //}

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
            if ((cword & ControlWord.R) != 0) // Read
            {
                if ((cword & ControlWord.WORD) != 0) // 16-bit
                    dbusWord = machine.RAM.ReadWord(ms.Word, abusWord);
                else
                    dbusWord = machine.RAM.ReadByte(ms.Word, abusWord);
            }
            else if ((cword & ControlWord.W) != 0) // Write
            {
                if ((cword & ControlWord.WORD) != 0) // 16-bit
                    machine.RAM.WriteWord(ms.Word, abusWord, dbusWord);
                else
                    machine.RAM.WriteByte(ms.Word, abusWord, (byte)(dbusWord & 0xFF));
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
            return null;
        }
    }

    [Flags]
    internal enum ControlWord : uint
    {
        None,
        IPC1    = 0x01,
        IPC2    = 0x02,
        IPO     = 0x03,
        II      = 0x04,
        OI      = 0x08,
        FI      = 0x0C,
        RTN     = 0x10,
        RSO1    = 0x20,
        RSO2    = 0x40,
        TAO     = 0x80,
        TBO     = 0x100,
        RSI1    = 0x200,
        TAI     = 0x400,
        TBI     = 0x600,
        R       = 0x800,
        W       = 0x1000,
        BYTE    = 0,
        WORD    = 0x2000,
        ADD     = 0x4000,
        SUB     = 0x8000,
        OR      = 0xC000,
        XOR     = 0x10000,
        AND     = 0x14000,
        SHL     = 0x18000,
        SHR     = 0x1C000,
        DATA    = 0,
        ADDR    = 0x20000,
        CS      = 0x40000,
        SS      = 0x80000,
        DS      = 0xC0000,
        HLT     = 0x100000,
        JMP     = 0x200000,
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
