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
                File.WriteAllBytes(@"C:\\Temp\\Cobalt.bin", data);
            }
            catch (AssemblyException ex)
            {
                Console.WriteLine($"Line {ex.Line}: {ex.Message}");
                return;
            }

            var machine = new Machine(microcodeRom) { DebugOutput = true, ShutdownWhenHalted = true };

            var assembler = new Assembler(microcodeRom);
            var program = assembler.AssembleSource(
                "nop\n" +
                @"
                jmp Main
                mov r2, 0x3333
                Wow:
                    mov r0, 0x1234
                    mov r1, 0x4321
                    ret
                Main:
                    mov sp, 0xF000
                    mov r0, 0x1111
                    call Wow
                    mov r3, 0x4444
                "
                + "hlt"
            );
            for (ushort i = 0; i < program.Length; ++i)
                machine.RAM.WriteByte(0, i, program[i]);

            File.WriteAllBytes(@"C:\\Temp\Program.bin", program);

            machine.Run();

            Console.ReadKey();
        }
    }
}