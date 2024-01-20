namespace Emulator
{
    internal static class Program
    {
        public static void Main(string[] args)
        {
            var config = RuntimeConfig.FromCommandLine(args);
            if (config == null)
            {
                RuntimeConfig.PrintHelp();
                return;
            }

            MicrocodeRom microcodeRom;
            try
            {
                microcodeRom = Microcode.AssembleRom(config.MicrocodePath!);
                var data = MicrocodeRom.ToRomBinary(microcodeRom);

                if (config.ExportArtifacts)
                    File.WriteAllBytes(@"C:\\Temp\\CobaltMicrocode.rom", data);
            }
            catch (AssemblyException ex)
            {
                Console.WriteLine($"Line {ex.Line}: {ex.Message}");
                return;
            }

            var bootRom = new Memory(0x0FFF);
            try
            {
                var bootSource = !string.IsNullOrEmpty(config.TestAsm)
                               ? $"nop\n{config.TestAsm}\nhlt"
                               : File.ReadAllText(config.BootAsmPath!);
                var bootAssembler = new Assembler(microcodeRom);
                var bootProgram = bootAssembler.AssembleSource(bootSource);
                for (ushort i = 0; i < bootProgram.Length; ++i)
                    bootRom.WriteByte(0, i, bootProgram[i]);
                bootRom.IsReadOnly = true;

                if (config.ExportArtifacts)
                    File.WriteAllBytes(@"C:\\Temp\\CobaltBoot.rom", bootProgram);
            }
            catch (AssemblyException ex)
            {
                Console.WriteLine($"Line {ex.Line}: {ex.Message}");
                return;
            }

            var machine = new Machine(microcodeRom, bootRom, config.Devices)
            {
                DebugOutput = config.DebugOutput,
                ShutdownWhenHalted = config.ShutdownWhenHalted
            };

            machine.Initialize();
            machine.Run();
            machine.Shutdown();

            Console.WriteLine("Execution complete");
            Console.ReadKey();
        }
    }
}