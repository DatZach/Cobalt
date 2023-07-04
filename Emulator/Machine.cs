namespace Emulator
{
    public sealed class Machine
    {
        public bool IsPowered { get; private set; }

        public bool ShutdownWhenHalted { get; set; }

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
                if (ShutdownWhenHalted)
                    IsPowered = false;

                Console.WriteLine("CPU Halted!");
                Console.WriteLine(CPU);

                Console.Write("     ");
                for (int j = 0; j < 16; ++j)
                    Console.Write($"{j:X2} ");
                Console.WriteLine();

                for (int i = 0; i < 16; ++i)
                {
                    Console.Write($"{(i * 16):X4} ");
                    for (int j = 0; j < 16; ++j)
                    {
                        Console.Write($"{RAM.ReadByte(0, (ushort)(i * 16 + j)):X2} ");
                    }

                    Console.WriteLine();
                }
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
