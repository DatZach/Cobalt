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

            var machine = new Machine(microcodeRom) { DebugOutput = true };

            var assembler = new Assembler(microcodeRom);
            var program = assembler.AssembleSource(
                "nop\n" +
                //"mov r0, 4660\n" +
                //"mov r1, r0\n" +
                //"mov r2, 50\n" +
                //"mov [64], r0\n" +
                "mov [64], 4660\n" +
                "mov [96], [64]\n" +
                //"mov r1, 50\n" +
                //"mov [r1+10], r0\n" +
                //"mov r2, [r1+10]\n" +
                
                //"mov r0, 123\n" +
                //"mov r1, 321\n" +
                //"add r0, r1\n" +
                //"mov r2, r0\n" +
                //"mov [100], r2\n" +
                //"mov r3, 125\n" +
                //"mov [r3+50], r2\n" +
                //"mov r1, [r3+50]\n" +
                //"mov [r3], r2\n" +
                //"mov [100], 555\n" +
                //"mov r0, [100]\n" +
                "hlt"
            );
            for (ushort i = 0; i < program.Length; ++i)
                machine.RAM.WriteByte(0, i, program[i]);

            File.WriteAllBytes(@"C:\\Temp\Program.bin", program);

            machine.Run();
        }
    }
}