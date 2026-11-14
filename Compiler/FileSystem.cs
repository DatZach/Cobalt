using System.Diagnostics;
using System.Text;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Compiler
{
    internal static class FileSystem
    {
        private static readonly Dictionary<string, string> Cache = new (StringComparer.OrdinalIgnoreCase);

        /*
         *  LOCAL FILESYSTEM
         */

        public static void WriteAllBytes(string path, byte[] bytes)
        {
            //if (Cache.ContainsKey(path))
            //    Cache[path] = contents;

            try
            {
                stopwatch.Start();
                File.WriteAllBytes(path, bytes);
            }
            finally
            {
                stopwatch.Stop();
            }
        }

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
            if (string.IsNullOrEmpty(path))
                return "";

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

        public static bool FileExists(string path)
        {
            if (Cache.TryGetValue(path, out _))
                return true;

            try
            {
                stopwatch.Start();
                return File.Exists(path);
            }
            finally
            {
                stopwatch.Stop();
            }
        }

        public static bool IsCached(string path)
        {
            return Cache.ContainsKey(path);
        }


        /*
         *  SERVER FILESYSTEM
         */

        public static void OpenDocument(string path, string content)
        {
            Cache[path] = content;
        }

        public static void CloseDocument(string path)
        {
            Cache.Remove(path);
        }

        public static void ChangeDocument(string path, string content, Range? range)
        {
            if (!Cache.TryGetValue(path, out var document))
                return;

            if (range == null)
                document = content;
            else
            {
                var startIndex = ResolveLineOffset(document, range.Start);
                var endIndex = ResolveLineOffset(document, range.End);
                var sb = new StringBuilder(document.Length);
                sb.Append(document[..startIndex]);
                sb.Append(content);
                sb.Append(document[endIndex..]);
                document = sb.ToString();
            }

            Cache[path] = document;

            return;

            static int ResolveLineOffset(string document, Position position)
            {
                int curLine = 0;
                int i = 0, j = 0;
                while (true)
                {
                    i = document.IndexOf('\n', i);
                    if (i == -1)
                        return document.Length;

                    if (curLine == position.Line)
                    {
                        j += position.Character;
                        return j >= i ? i : j;
                    }

                    j = ++i;
                    ++curLine;
                }
            }
        }

        public static long TotalMilliseconds => stopwatch.ElapsedMilliseconds;
        private static readonly Stopwatch stopwatch = new ();
    }
}
