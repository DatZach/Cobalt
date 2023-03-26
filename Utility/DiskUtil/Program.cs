using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DiskUtil
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            var config = RuntimeConfig.FromCommandLine(args);
            if (config == null)
            {
                RuntimeConfig.PrintHelp();
                return;
            }

            using var disk = Disk.FromFile(config.DiskPath, config.BytesPerSector);
            var cobalt = new Cobalt(disk);

            switch (config.Operation)
            {
                case Operation.Format:
                {
                    var stage1 = config.Stage1Path != null ? File.ReadAllBytes(config.Stage1Path) : null;
                    var stage2 = config.Stage2Path != null ? File.ReadAllBytes(config.Stage2Path) : null;
                    cobalt.Format(config.VolumeName, config.SectorsPerCluster, config.ClustersPerBlock, stage1, stage2);
                    break;
                }

                default:
                    throw new ArgumentOutOfRangeException("Operation");
            }
        }
    }

    internal sealed class RuntimeConfig
    {
        public string DiskPath { get; init; }

        public string SourcePath { get; init; }

        public Operation Operation { get; init; }

        public string DestinationPath { get; init; }

        public int BytesPerSector { get; init; }

        public string? VolumeName { get; init; }

        public int SectorsPerCluster { get; init; }

        public int ClustersPerBlock { get; init; }

        public string? Stage1Path { get; init; }

        public string? Stage2Path { get; init; }

        private RuntimeConfig()
        {
            // NOTE Private ctor to enforce factory pattern
        }

        public static RuntimeConfig? FromCommandLine(string[] args)
        {
            if (args.Length < 2)
                return null;

            return new RuntimeConfig
            {
                DiskPath = args[0],
                Operation = Enum.Parse<Operation>(args[1]),
                BytesPerSector = OptionalArgument("--bytes-per-sector", 512),
                VolumeName = OptionalArgument<string>("--volume-name", "My Volume"),
                SectorsPerCluster = OptionalArgument("--sectors-per-cluster", 8), // 4kb
                ClustersPerBlock = OptionalArgument("--clusters-per-block", 262144), // 1gb
                Stage1Path = OptionalArgument<string>("--stage1"),
                Stage2Path = OptionalArgument<string>("--stage2")
            };

            T? OptionalArgument<T>(string key, T? fallback = default)
            {
                var value = args.FirstOrDefault(x => x.StartsWith(key))?.Split('=').ElementAtOrDefault(1);
                if (value == null)
                    return fallback;

                return (T)Convert.ChangeType(value, typeof(T));
            }
        }

        public static void PrintHelp()
        {
            Console.WriteLine("DiskUtil <DiskPath> <Operation> <SourceFilePath> <DestinationPath>");
            Console.WriteLine("\tOperations = CopyBin, CopyFile");
        }
    }

    internal enum Operation
    {
        None,
        Format
    }
}
