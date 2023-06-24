namespace Emulator
{
    internal static class Program
    {
        public static void Main(string[] args)
        {
            MicrocodeAssembler.CompileFile(
                "C:\\Users\\zreedy\\Dropbox\\Cobalt\\Microcode.cmc",
                "C:\\Temp\\Cobalt.bin"
            );

            return;

            var machine = new Machine();

            while (machine.IsPowered)
            {
                machine.Tick();
            }
        }
    }
}