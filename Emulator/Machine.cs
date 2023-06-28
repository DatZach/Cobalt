using System.Reflection.PortableExecutable;

namespace Emulator
{
    internal sealed class Machine
    {
        public bool IsPowered { get; private set; }

        public CPU CPU { get; }

        public RAM RAM { get; }

        public Machine(MicrocodeRom microcode)
        {
            CPU = new CPU(this, microcode);
            RAM = new RAM(16 * 1024 * 1024);

            // TEMP ///
            var program = new byte[]
            {
                0x00, 0x00, 0x1D, 0x30, 0x7D, 0x00, 0x1D, 0x31, 0x41, 0x01, 0x25, 0x20, 0x01, 0x18
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
                Console.WriteLine(CPU);
            }
        }

        public void Run()
        {
            // TODO Run at specified Hz
            while (IsPowered)
            {
                Tick();
            }
        }
    }
}
