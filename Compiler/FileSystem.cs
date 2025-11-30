using System.Diagnostics;

namespace Compiler
{
    internal static class FileSystem
    {
        private static readonly Dictionary<string, string> Cache = new (StringComparer.OrdinalIgnoreCase);

        public static void WriteAllText(string path, string contents)
        {
            if (Cache.ContainsKey(path))
                Cache[path] = contents;

            try
            {
                stopwatch.Start();
                File.WriteAllText(path, contents);
            }
            finally
            {
                stopwatch.Stop();
            }
        }

        public static string ReadAllText(string path)
        {
            if (Cache.TryGetValue(path, out var contents))
                return contents;

            try
            {
                stopwatch.Start();
                contents = File.ReadAllText(path);
                Cache.Add(path, contents);

                return contents;
            }
            finally
            {
                stopwatch.Stop();
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

        public static void Copy(string source, string destination, bool overwrite)
        {
            try
            {
                stopwatch.Start();
                File.Copy(source, destination, overwrite);
            }
            finally
            {
                stopwatch.Stop();
            }
        }

        public static void Delete(string path)
        {
            try
            {
                stopwatch.Start();
                File.Delete(path);
            }
            finally
            {
                stopwatch.Stop();
            }
        }

        public static long TotalMilliseconds => stopwatch.ElapsedMilliseconds;
        private static readonly Stopwatch stopwatch = new ();
    }
}
