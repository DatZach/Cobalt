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

            var machine = new Machine(microcodeRom);

            var assembler = new Assembler(microcodeRom);
            var program = assembler.AssembleSource(
                "nop\n" +
                "mov r0, 123\n" +
                "mov r1, 321\n" +
                "add r0, r1\n" +
                "hlt"
            );
            for (ushort i = 0; i < program.Length; ++i)
                machine.RAM.WriteByte(0, i, program[i]);

            machine.Run();
        }
    }
}