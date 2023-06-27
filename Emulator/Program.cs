namespace Emulator
{
    internal static class Program
    {
        public static void Main(string[] args)
        {
            ControlWord[] microcode;
            try
            {
                var microcodeRom = Microcode.AssembleRom("Microcode.cmc");
                microcode = Microcode.ControlWordsFromRomBinary(microcodeRom);
                File.WriteAllBytes(@"C:\\Temp\\Cobalt.bin", microcodeRom);
            }
            catch (AssemblyException ex)
            {
                Console.WriteLine($"Line {ex.Line}: {ex.Message}");
                return;
            }

            var machine = new Machine(microcode);
            machine.Run();
        }
    }
}