namespace Emulator
{
    internal static class Program
    {
        public static void Main(string[] args)
        {
            MicrocodeRom microcodeRom;
            try
            {
                microcodeRom = Microcode.AssembleRom("Microcode.cmc");
                var data = MicrocodeRom.ToRomBinary(microcodeRom);
                File.WriteAllBytes(@"C:\\Temp\\Cobalt.bin", data);
            }
            catch (AssemblyException ex)
            {
                Console.WriteLine($"Line {ex.Line}: {ex.Message}");
                return;
            }

            var machine = new Machine(microcodeRom);
            machine.Run();
        }
    }
}