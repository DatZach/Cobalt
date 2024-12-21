using System.Diagnostics;

namespace Emulator.Devices
{
    internal sealed class RtcDevice : DeviceBase<RtcDevice.ConfigDefinition>
    {
        public override string Name => "Real Time Clock";

        public override short DevAddrLo => 0x01;

        public override short DevAddrHi => 0x08;

        private const int CTRL_TE = 0x80;
        private const int CTRL_CS = 0x40;
        private const int CTRL_BME = 0x20;
        private const int CTRL_TPE = 0x10;
        private const int CTRL_TIE = 0x08;
        private const int CTRL_KIE = 0x04;
        private const int CTRL_WDE = 0x02;
        private const int CTRL_WDS = 0x01;

        private byte regCtrlB;
        
        private DateTime timestamp;
        private long lastTsTick;

        public RtcDevice()
        {
            timestamp = DateTime.Now;
            lastTsTick = Stopwatch.GetTimestamp();
            regCtrlB = CTRL_TE;
        }

        public override bool Tick()
        {
            var machine = Machine;
            if (machine.TickIndex % machine.ClockHz == 0)
            {
                var nowTsTick = Stopwatch.GetTimestamp();
                if ((regCtrlB & CTRL_TE) == CTRL_TE)
                    timestamp = timestamp.AddTicks(nowTsTick - lastTsTick);
                lastTsTick = nowTsTick;
            }

            return false;
        }

        public override byte ReadByte(ushort segment, ushort offset)
        {
            if (offset == 7)
                return regCtrlB;

            var value = offset switch
            {
                0 => timestamp.Second,
                1 => timestamp.Minute,
                2 => timestamp.Hour,
                3 => (int)timestamp.DayOfWeek + 1,
                4 => timestamp.Day,
                5 => timestamp.Month,
                6 => timestamp.Year % 100,
                _ => 0
            };

            return (byte)((((value / 10) & 0x0F) << 4) | ((value % 10) & 0x0F));
        }

        public override ushort ReadWord(ushort segment, ushort offset)
        {
            return ReadByte(segment, offset);
        }

        public override void WriteByte(ushort segment, ushort offset, byte value)
        {
            if (offset == 7)
                regCtrlB = value;
            else
            {
                value = (byte)((value >> 4) * 10 + (value & 0x0F));

                var year   = offset == 6 ? value : timestamp.Year;
                var month  = offset == 5 ? value : timestamp.Month;
                var day    = offset == 4 ? value : timestamp.Day;
                var hour   = offset == 2 ? value : timestamp.Hour;
                var minute = offset == 1 ? value : timestamp.Minute;
                var second = offset == 0 ? value : timestamp.Second;
                
                timestamp = new DateTime(year, month, day, hour, minute, second);
            }
        }

        public override void WriteWord(ushort segment, ushort offset, ushort value)
        {
            WriteByte(segment, offset, (byte)value);
        }

        public sealed class ConfigDefinition : DeviceConfigBase
        {

        }
    }
}
