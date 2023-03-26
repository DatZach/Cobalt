using System;

namespace DiskUtil
{
    internal static class DateUtility
    {
        private readonly static DateTime CobaltEpoch = new (2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static int ToCobaltTime(this DateTime time)
        {
            return (int)(time - CobaltEpoch).TotalSeconds;
        }

        public static DateTime FromCobaltTime(this int time)
        {
            return CobaltEpoch.AddSeconds(time);
        }
    }
}
