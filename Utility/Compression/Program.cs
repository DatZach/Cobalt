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

            switch (Config.Operation)
            {
                case Operation.Pack:
                    Archive.Pack(Config.ArchivePath, Config.SourcePathOrDirectories);
                    break;
                case Operation.Unpack:
                {
                    using var archive = Archive.OpenRead(Config.ArchivePath);
                    archive.Root.Extract(Config.SourcePathOrDirectories[0]);
                    break;
                }
                case Operation.Touch:
                    break;
                case Operation.Ls:
                {
                    using var archive = Archive.OpenRead(Config.ArchivePath);
                    var node = archive.Root.Find(Config.VirtualPathOrDirectory);

                    if (node == archive.Root && Config.Details)
                    {
                        Console.WriteLine($"Archive created {node.CreationTimeUtc:yyyy-MM-dd hh:mm:ss tt}");
                        Console.WriteLine($"       modified {node.LastModifiedTimeUtc:yyyy-MM-dd hh:mm:ss tt}");
                        if (!string.IsNullOrWhiteSpace(node.Name))
                            Console.WriteLine($"            for {node.Name}");
                        Console.WriteLine();
                    }

                    var columnWidth = node.Entries.Max(x => x.Name.Length) + 2;
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
                            sb.Append($"{entry.LastModifiedTimeUtc:yyyy-MM-dd hh:mm:ss tt}  ");
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
                    break;
                }
                case Operation.Cat:
                {
                    var archive = Archive.OpenRead(Config.ArchivePath);
                    
                    var node = archive.Root.Find(Config.VirtualPathOrDirectory);
                    if (node == null)
                    {
                        Console.WriteLine("The specified path does not exist.");
                        return;
                    }
                    else if (node.IsDirectory)
                    {
                        Console.WriteLine("Cannot 'cat' a directory.");
                        return;
                    }

                    var compressedBuffer = node.GetCompressedBuffer();
                    if (compressedBuffer == null)
                    {
                        Console.WriteLine("Unable to read compressed buffer");
                        return;
                    }

                    var decompressedBuffer = Cobpression.Decode(compressedBuffer);
                    using var stdout = Console.OpenStandardOutput();
                    stdout.Write(decompressedBuffer, 0, decompressedBuffer.Length);
                    break;
                }
                case Operation.Add:
                    break;
                case Operation.Rm:
                    break;
                case Operation.Stat:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    internal sealed class RuntimeConfig
    {
        public Operation Operation { get; init; }

        public string ArchivePath { get; init; }

        public IReadOnlyList<string>? SourcePathOrDirectories { get; init; }

        public string? VirtualPathOrDirectory { get; init; }

        public bool Raw { get; init; }

        public bool Verify { get; init; }

        public bool Details { get; init; }

        public string? Comment { get; init; }

        public int StatisticsVerboseOutputLevel { get; init; }

        private RuntimeConfig()
        {
            // NOTE Private ctor to enforce factory pattern
        }

        public static RuntimeConfig? FromCommandLine(string[] args)
        {
            if (args.Length < 3)
                return null;

            // car pack <archive> <src>+ [--raw]
            //  echo "Wow, neat!" > car pack stdout stdin --raw
            // car unpack <archive> <dir> [--verify]
            // car touch <archive>
            // car browse <archive>
            // car ls <archive> <ar-dir>
            // car cat <archive> <ar-path> [--verify]
            // car add <archive> <ar-dir> <src>+
            // car rm <archive> <ar-path/dir>
            // car stat <archive> <ar-path> [--verify]

            if (!Enum.TryParse<Operation>(args[0], true, out var operation))
                return null;

            var srcCount = args.Length - 2;
            var isRaw = OptionalArgument("--raw", false);
            if (isRaw) --srcCount;
            var isVerify = OptionalArgument("--verify", false);
            if (isVerify) --srcCount;
            var isDetails = OptionalArgument("--details", false);
            if (isDetails) --srcCount;
            var comment = OptionalArgument<string>("--comment", null);
            if (comment != null) --srcCount;
            var statisticsVerboseOutputLevel = OptionalArgument("--stats-verbose", 0);
            if (statisticsVerboseOutputLevel > 0) --srcCount;

            return new RuntimeConfig
            {
                Operation = operation,
                ArchivePath = args[1],
                VirtualPathOrDirectory = args.ElementAtOrDefault(2),
                SourcePathOrDirectories = args.Skip(2).Take(srcCount).ToList(),
                Details = isDetails,
                Comment = comment,
                Raw = isRaw,
                Verify = isVerify,
                StatisticsVerboseOutputLevel = statisticsVerboseOutputLevel
            };

            T? OptionalArgument<T>(string key, T? fallback = default)
            {
                var arg = args.FirstOrDefault(x => x.StartsWith(key));
                if (arg != null && typeof(T) == typeof(bool))
                    return (T)(object)true;

                var value = arg?.Split('=').ElementAtOrDefault(1);
                if (value == null)
                    return fallback;

                return (T)Convert.ChangeType(value, typeof(T));
            }
        }

        public static void PrintHelp()
        {
            Console.WriteLine("car <pack | unpack> <src> <dst>+ [--raw]");
        }
    }

    internal enum Operation
    {
        Pack,
        Unpack,
        Touch,
        Ls,
        Cat,
        Add,
        Rm,
        Stat
    }
}
