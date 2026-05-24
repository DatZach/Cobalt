namespace Compression
{
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

            if (!Enum.TryParse<Operation>(args[0], true, out var operation))
                return null;

            var optionsIdx = Array.IndexOf(args, args.FirstOrDefault(x => x.StartsWith("--")));
            if (optionsIdx == -1) optionsIdx = args.Length;

            string? virtualPathOrDirectory;
            List<string> sourcePathOrDirectories;
            if (operation == Operation.Pack)
            {
                virtualPathOrDirectory = null;
                sourcePathOrDirectories = args.Skip(2).Take(optionsIdx - 2).ToList();
            }
            else
            {
                virtualPathOrDirectory = args[2];
                sourcePathOrDirectories = args.Skip(3).Take(optionsIdx - 3).ToList();
            }

            var raw = OptionalArgument("--raw", false);
            var verify = OptionalArgument("--verify", false);
            var details = OptionalArgument("--details", false);
            var comment = OptionalArgument<string>("--comment", null);
            var statisticsVerboseOutputLevel = OptionalArgument("--stats-verbose", 0);

            return new RuntimeConfig
            {
                Operation = operation,
                ArchivePath = args[1],
                VirtualPathOrDirectory = virtualPathOrDirectory,
                SourcePathOrDirectories = sourcePathOrDirectories,
                Details = details,
                Comment = comment,
                Raw = raw,
                Verify = verify,
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
            Console.WriteLine(
                $"Cobalt ARchive / Cobpression (v{Archive.Version:X4})\r\n" +
                "car pack <archive> <src>* [--raw] [--comment=\"\"]\r\n" +
                "\techo \"Wow, neat!\" > car pack stdout stdin --raw\r\n" +
                "car unpack <archive> <host-dir> [--verify]\r\n" +
                "car ls <archive> <virt-dir>\r\n" +
                "car cat <archive> <virt-path> [--verify]\r\n" +
                "car add <archive> <virt-dir> <src>+\r\n" +
                "car rm <archive> <virt-path/dir>\r\n" +
                "car stat <archive> <virt-path> [--verify]"
            );
        }
    }

    internal enum Operation
    {
        Pack,
        Unpack,
        Ls,
        Cat,
        Add,
        Rm,
        Stat
    }
}
