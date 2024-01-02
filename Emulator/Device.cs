using System.Diagnostics;

namespace Emulator
{
    public abstract class Device
    {
        public Machine Machine { get; init; }

        public abstract string Name { get; }

        public abstract void Initialize();

        public abstract void Shutdown();

        public abstract bool Tick();
    }

    public sealed class PITDevice : Device
    {
        public override string Name => "Programmable Interrupt Timer";

        private readonly Stopwatch stopwatch;

        private ushort RegFireEveryXSeconds
        {
            get => Machine.ReadWord(0, 0x7F80);
            set => Machine.WriteWord(0, 0x7F80, value);
        }
        private bool RegIsFired
        {
            get => Machine.ReadWord(0, 0x7F82) != 0;
            set => Machine.WriteWord(0, 0x7F82, (ushort)(value ? 1 : 0));
        }

        private bool prevRegIsFired;

        public PITDevice()
        {
            stopwatch = new Stopwatch();
        }

        public override void Initialize()
        {
            RegFireEveryXSeconds = 1;
            stopwatch.Start();
        }

        public override void Shutdown()
        {
            stopwatch.Stop();
        }

        public override bool Tick()
        {
            if (prevRegIsFired && !RegIsFired)
                stopwatch.Restart();
            if (stopwatch.Elapsed.Seconds >= RegFireEveryXSeconds)
                RegIsFired = true;

            prevRegIsFired = RegIsFired;

            return RegIsFired;
        }
    }

    public sealed class TTLDevice : Device
    {
        public override string Name => "Teletype";

        private char OutChar
        {
            get => (char)Machine.ReadWord(0, 0xFF84);
            set => Machine.WriteWord(0, 0xFF84, (byte)value);
        }

        public override void Initialize()
        {
            
        }

        public override void Shutdown()
        {
            
        }

        public override bool Tick()
        {
            if (OutChar != '\0')
            {
                Console.Write(OutChar);
                OutChar = '\0';
            }

            return false;
        }
    }
}
