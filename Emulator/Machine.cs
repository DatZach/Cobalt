using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Emulator
{
    internal sealed class Machine
    {
        public bool IsPowered { get; private set; }

        public CPU CPU { get; }

        public RAM RAM { get; }

        public Machine()
        {
            CPU = new CPU(this);
            RAM = new RAM(16 * 1024 * 1024);

            // TEMP ///
            var program = new byte[]
            {
                0xB8, 0x10, 0x00, 0x7B, 0xB8, 0x11, 0x01, 0x41, 0xC0, 0x00, 0x01, 0x06
            };
            for (ushort i = 0; i < program.Length; ++i)
                RAM.WriteByte(0, i, program[i]);
            // TEMP ///

            IsPowered = true;
        }

        public void Tick()
        {
            var isCpuHalted = CPU.IsHalted;

            CPU.Tick();

            if (!isCpuHalted && CPU.IsHalted)
            {
                Console.WriteLine("CPU Halted!");
                Console.WriteLine($"r0 = {CPU.r0.Word:X4} r1 = {CPU.r1.Word:X4}");
            }
        }
    }

    internal sealed class RAM
    {
        public int Size => data.Length;

        private readonly byte[] data;

        public RAM(int size)
        {
            data = new byte[size];
        }

        public byte ReadByte(ushort segment, ushort offset) => data[ToCanonicalAddress(segment, offset)];

        public void WriteByte(ushort segment, ushort offset, byte value) => data[ToCanonicalAddress(segment, offset)] = value;

        public ushort ReadWord(ushort segment, ushort offset) =>
            (ushort)((data[ToCanonicalAddress(segment, (ushort)(offset + 0))] << 8)
                     | data[ToCanonicalAddress(segment, (ushort)(offset + 1))]);

        public void WriteWord(ushort segment, ushort offset, ushort value)
        {
            data[ToCanonicalAddress(segment, (ushort)(offset +  1))] = (byte)(value & 0xFF);
            data[ToCanonicalAddress(segment, (ushort)(offset +  0))] = (byte)((value >> 8) & 0xFF);
        }

        private static long ToCanonicalAddress(ushort segment, ushort offset)
        {
            return ((long)segment << 16) | offset;
        }
    }
}
