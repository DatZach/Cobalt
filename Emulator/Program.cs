namespace Emulator
{
    internal static class Program
    {
        public static void Main(string[] args)
        {
            var machine = new Machine();

            while (machine.IsPowered)
            {
                machine.Tick();
            }
        }
    }
}