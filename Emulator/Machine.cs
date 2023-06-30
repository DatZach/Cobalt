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
