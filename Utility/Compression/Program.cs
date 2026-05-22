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
                    Archive.Unpack(Config.ArchivePath, Config.SourcePathOrDirectories[0]);
                    break;
                case Operation.Touch:
                    break;
                case Operation.Browse:
                    break;
                case Operation.Ls:
                    break;
                case Operation.Cat:
                    break;
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
            var isRaw = OptionalArgument("raw", false);
            if (isRaw) --srcCount;
            var isVerify = OptionalArgument("verify", false);
            if (isVerify) --srcCount;

            return new RuntimeConfig
            {
                Operation = operation,
                ArchivePath = args[1],
                VirtualPathOrDirectory = args.ElementAtOrDefault(2),
                SourcePathOrDirectories = args.Skip(2).Take(srcCount).ToList(),
                Raw = isRaw,
                Verify = isVerify
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
        Browse,
        Ls,
        Cat,
        Add,
        Rm,
        Stat
    }
}
