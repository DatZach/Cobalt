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
                var bootSource = File.ReadAllText(config.BootAsmPath!);
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

            var machine = new Machine(microcodeRom, bootRom)
            {
                DebugOutput = config.DebugOutput,
                ShutdownWhenHalted = config.ShutdownWhenHalted
            };

            if (!string.IsNullOrEmpty(config.TestAsm))
            {
                var assembler = new Assembler(microcodeRom);
                var program = assembler.AssembleSource($"nop\n{config.TestAsm}\nhlt");

                bootRom.IsReadOnly = false;
                for (ushort i = 0; i < program.Length; ++i)
                    bootRom.WriteByte(0, i, program[i]);
                bootRom.IsReadOnly = true;
            }
            else
            {
                // TODO Would be nice to do this via reflection...
                foreach (var deviceConfig in config.Devices)
                {
                    if (deviceConfig is AtaDevice.ConfigDefinition ataConfig)
                        machine.AddDevice<AtaDevice, AtaDevice.ConfigDefinition>(ataConfig);
                    else if (deviceConfig is KeyboardDevice.ConfigDefinition kbdConfig)
                        machine.AddDevice<KeyboardDevice, KeyboardDevice.ConfigDefinition>(kbdConfig);
                    else if (deviceConfig is VideoDevice.ConfigDefinition videoConfig)
                        machine.AddDevice<VideoDevice, VideoDevice.ConfigDefinition>(videoConfig);
                    else
                        throw new NotImplementedException();
                }
            }

            machine.Initialize();
            machine.Run();
            machine.Shutdown();

            Console.WriteLine("Execution complete");
            Console.ReadKey();
        }
    }
}