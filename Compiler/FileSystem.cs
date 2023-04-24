using System.Diagnostics;

namespace Compiler
{
    internal static class FileSystem
    {
        private readonly static Dictionary<string, string> Cache = new (StringComparer.OrdinalIgnoreCase);
        private readonly static Stopwatch Stopwatch = new ();

        public static long TotalIOMilliseconds => Stopwatch.ElapsedMilliseconds;

        public static string ReadAllText(string path)
        {
            if (Cache.TryGetValue(path, out var contents))
                return contents;

            try
            {
                Stopwatch.Start();
                contents = File.ReadAllText(path);
                Cache.Add(path, contents);

                return contents;
            }
            finally
            {
                Stopwatch.Stop();
            }
        }

        public static string? GetLine(string path, int lineIndex)
        {
            var content = ReadAllText(path);

            int idx = 0;
            int i = 0;
            while (i < content.Length)
            {
                int j = content.IndexOf('\n', i);

                if (idx == lineIndex)
                    return content.Substring(i, j - i);

                i = j + 1;
                ++idx;
            }

            return null;
        }
    }
}
