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
                mov r0, 12
                mov r1, 3
                mul r0, r1
                mov r2, -12
                mul r2, r1
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