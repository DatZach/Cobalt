namespace Emulator
{
    internal static class Program
    {
        public static void Main(string[] args)
        {
            MicrocodeRom microcodeRom;
            try
            {
                microcodeRom = Microcode.AssembleRom(@"X:\\Cobalt\\Emulator\\Microcode.cmc");
                var data = MicrocodeRom.ToRomBinary(microcodeRom);
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
                var bootSource = File.ReadAllText(@"X:\\Cobalt\\Emulator\\Boot.cas");
                var bootAssembler = new Assembler(microcodeRom);
                var bootProgram = bootAssembler.AssembleSource(bootSource);
                for (ushort i = 0; i < bootProgram.Length; ++i)
                    bootRom.WriteByte(0, i, bootProgram[i]);
                bootRom.IsReadOnly = true;
                File.WriteAllBytes(@"C:\\Temp\\CobaltBoot.rom", bootProgram);
            }
            catch (AssemblyException ex)
            {
                Console.WriteLine($"Line {ex.Line}: {ex.Message}");
                return;
            }

            var machine = new Machine(microcodeRom, bootRom) { DebugOutput = false, ShutdownWhenHalted = false };
            machine.AddDevice<VideoDevice>();
            machine.AddDevice<KeyboardDevice>();
            //machine.AddDevice<PITDevice>();
            //machine.AddDevice<TTLDevice>();

//            var assembler = new Assembler(microcodeRom);
//            var program = assembler.AssembleSource(
//                "nop\nnop\n" +
//                @"
//jmp Main
//IRQ:
//    mov [0xFF84], 0x21 ; !
//    ;mov r0, [0xFF80]
//    ;add r0, 1
//    ;mov [0xFF80], r0
//    mov [0xFF82], 0
//    rti
//Main:
//    mov sp, 0xFFF0
//    mov [0x0000], IRQ
//    mov [0xFF84], 0x48 ; H
//    mov [0xFF84], 0x45 ; E
//    mov [0xFF84], 0x4C ; L
//    mov [0xFF84], 0x4C ; L
//    mov [0xFF84], 0x4F ; O
//Loop:
//    jmp Loop
//                "
//                + "hlt"
//            );
//            for (ushort i = 0; i < program.Length; ++i)
//                machine.RAM.WriteByte(0, i, program[i]);

//            File.WriteAllBytes(@"C:\\Temp\Program.bin", program);

            machine.Initialize();
            machine.Run();
            machine.Shutdown();

            Console.WriteLine("Execution complete");
            Console.ReadKey();
        }
    }
}