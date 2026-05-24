using System.IO.Hashing;
using System.Text;
using Compression.Utility;

namespace Compression
{
    public static class Program
    {
        internal static RuntimeConfig Config { get; private set; }

        public static void Main(string[] args)
        {
            Config = RuntimeConfig.FromCommandLine(args);
            if (Config == null)
            {
                RuntimeConfig.PrintHelp();
                return;
            }

            try
            {
                switch (Config.Operation)
                {
                    case Operation.Pack: Pack(); break;
                    case Operation.Unpack: Unpack(); break;
                    case Operation.Ls: Ls(); break;
                    case Operation.Cat: Cat(); break;
                    case Operation.Add: Add(); break;
                    case Operation.Rm: Rm(); break;
                    case Operation.Stat: Stat(); break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
            }
        }

        private static void Pack()
        {
            using var archive = Archive.OpenCreateNew(Config.ArchivePath, Config.Comment);
            foreach (var sourcePathOrDirectory in Config.SourcePathOrDirectories)
            {
                var entry = Archive.Entry.NewFromFileSystem(sourcePathOrDirectory);
                archive.Root.Add(entry);
            }

            archive.Commit();
        }

        private static void Unpack()
        {
            using var archive = Archive.OpenExisting(Config.ArchivePath);
            archive.Root.Extract(Config.SourcePathOrDirectories[0]);
        }

        private static void Ls()
        {
            using var archive = Archive.OpenExisting(Config.ArchivePath);
            var node = archive.Root.Find(Config.VirtualPathOrDirectory);

            if (node == archive.Root && Config.Details)
            {
                Console.WriteLine($"Archive created {node.CreationTimeUtc.ToLocalTime():yyyy-MM-dd hh:mm:ss tt}");
                Console.WriteLine($"       modified {node.LastModifiedTimeUtc.ToLocalTime():yyyy-MM-dd hh:mm:ss tt}");
                if (!string.IsNullOrWhiteSpace(node.Name))
                    Console.WriteLine($"            for {node.Name}");
                Console.WriteLine();
            }

            var columnWidth = node.Entries.Count == 0 ? Console.WindowWidth : node.Entries.Max(x => x.Name.Length) + 2;
            var columnsPerRow = Config.Details ? 1 : Math.Max(1, Console.WindowWidth / columnWidth);
            var totalRows = Math.Max(1, node.Entries.Count / columnsPerRow);
            if (totalRows == 1) columnWidth = 0;
            var hasNl = false;
            int fileCount = 0, totalFileSize = 0;
            var dirCount = 0;

            for (var i = 0; i < node.Entries.Count; ++i)
            {
                var entry = node.Entries[i];
                var sb = new StringBuilder(columnWidth);

                if (Config.Details)
                {
                    sb.Append($"{entry.LastModifiedTimeUtc.ToLocalTime():yyyy-MM-dd hh:mm:ss tt}  ");
                    if (entry.IsDirectory)
                        sb.Append(new string(' ', 9 + 2 + 8 + 2));
                    else
                    {
                        sb.Append($"{entry.UncompressedSize.ToFriendlyFileSize(),9}  ");
                        sb.Append($"{entry.OriginalChecksumCRC32:X8}  ");
                    }
                }

                if (entry.IsDirectory)
                {
                    sb.Append($"%{entry.Name}%/");
                    ++dirCount;
                }
                else
                {
                    sb.Append(entry.Name);
                    ++fileCount;
                    totalFileSize += entry.UncompressedSize;
                }

                var line = sb.ToString();
                var lineLength = line.Length;
                var fg = Console.ForegroundColor;
                var flip = false;
                foreach (var part in line.Split('%'))
                {
                    Console.ForegroundColor = flip ? ConsoleColor.Blue : fg;
                    Console.Write(part);
                    --lineLength;
                    flip = !flip;
                }
                Console.ForegroundColor = fg;
                Console.Write(new string(' ', Math.Max(2, columnWidth - lineLength)));
                if (((i+1) % columnsPerRow) == 0)
                {
                    Console.WriteLine();
                    hasNl = true;
                }
            }
            if (!hasNl) Console.WriteLine();

            if (Config.Details)
            {
                Console.WriteLine();
                Console.Write(new string(' ', 24));
                Console.WriteLine($"{totalFileSize.ToFriendlyFileSize(),9}            {fileCount} {(fileCount == 1 ? "File" : "Files")}");
                Console.Write(new string(' ', 45));
                Console.WriteLine($"{dirCount} {(dirCount == 1 ? "Directory" : "Directories")}");
            }
        }

        private static void Cat()
        {
            var archive = Archive.OpenExisting(Config.ArchivePath);
                    
            var node = archive.Root.Find(Config.VirtualPathOrDirectory);
            if (node == null)
                throw new Exception("The specified path does not exist.");
            else if (node.IsDirectory)
                throw new Exception("Cannot 'cat' a directory.");

            var buffer = node.UncompressedBuffer;
            if (buffer == null)
                throw new Exception("Unable to discover uncompressed stream.");

            using var stdout = Console.OpenStandardOutput();
            stdout.Write(buffer, 0, buffer.Length);
        }

        private static void Add()
        {
            using var archive = Archive.OpenExisting(Config.ArchivePath);
            var node = archive.Root.Find(Config.VirtualPathOrDirectory);
            if (node == null)
                throw new Exception("The specified path does not exist.");

            foreach (var sourcePathOrDirectory in Config.SourcePathOrDirectories)
            {
                var entry = Archive.Entry.NewFromFileSystem(sourcePathOrDirectory);
                node.Add(entry);
            }

            archive.Commit();
        }

        private static void Rm()
        {
            var parts = Config.VirtualPathOrDirectory.Split('/');
            var directory = string.Join('/', parts.Take(parts.Length - 1));
            var path = parts[^1];

            using var archive = Archive.OpenExisting(Config.ArchivePath);
            var node = archive.Root.Find(directory);
            if (node == null)
                throw new Exception("The specified path does not exist.");

            node.Remove(path);

            archive.Commit();
        }

        private static void Stat()
        {
            using var archive = Archive.OpenExisting(Config.ArchivePath);
            var node = archive.Root.Find(Config.VirtualPathOrDirectory);
            if (node == null)
                throw new Exception("The specified path does not exist.");

            var parts = Config.VirtualPathOrDirectory.Split('/');
            var directory = string.Join('/', parts.Take(parts.Length - 1));

            if (node.IsDirectory)
                Console.WriteLine($"Directory: {node.Name}");
            else
                Console.WriteLine($"     File: {node.Name}");
            
            Console.WriteLine($" Location: {directory}");

            if (!node.IsDirectory)
            {
                try
                {
                    var uncompressedBuffer = node.UncompressedBuffer;
                    var compressedBuffer = node.CompressedBuffer;
                    var uncompressedSize = uncompressedBuffer?.Length ?? 0;
                    var compressedSize = compressedBuffer?.Length ?? 0;

                    var uncompressedChecksumCRC32 = Crc32.HashToUInt32(uncompressedBuffer);
                    var checksumsMatched = node.OriginalChecksumCRC32 == uncompressedChecksumCRC32;

                    Console.WriteLine($"     Size: {compressedSize.ToFriendlyFileSize()} / {uncompressedSize.ToFriendlyFileSize()} ({compressedSize / (float)uncompressedSize:F2})");
                    Console.WriteLine($"  Checkum: {node.OriginalChecksumCRC32:X8} ({(checksumsMatched ? "Good" : $"MISMATCH {node.OriginalChecksumCRC32} != {uncompressedChecksumCRC32}")})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"     Size: ERROR ({ex.Message})");
                    Console.WriteLine($"  Checkum: ERROR");
                }
            }

            Console.WriteLine($"  Created: {node.CreationTimeUtc.ToLocalTime():yyyy-MM-dd hh:mm:ss tt}");
            Console.WriteLine($" Modified: {node.LastModifiedTimeUtc.ToLocalTime():yyyy-MM-dd hh:mm:ss tt}");
            Console.WriteLine($" Accessed: {node.LastAccessTimeUtc.ToLocalTime():yyyy-MM-dd hh:mm:ss tt}");
        }
    }
}
