namespace Compression.Utility
{
    internal static class StringUtility
    {
        private readonly static string[] fileSizeSuffix = ["b", "KB", "MB", "GB", "TB", "PB"];
        public static string ToFriendlyFileSize(this int value) => ToFriendlyFileSize((long)value);
        public static string ToFriendlyFileSize(this long value)
        {
            if (value == 0)
                return "0b";

            var e = (int)Math.Log(value, 1024);
            return $"{value / Math.Pow(1024, e):F2} {fileSizeSuffix[e]}";
        }
    }
}
