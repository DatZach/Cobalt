using System.Diagnostics;
using Compiler.Lexer;

namespace Compiler
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            var t1 = Stopwatch.StartNew();

            var config = RuntimeConfig.FromCommandLine(args);
            if (config == null)
            {
                RuntimeConfig.PrintHelp();
                return;
            }

            var source = File.ReadAllText(config.EntrySourceFile);
            var tokenizer = new Tokenizer(source);
            var tokens = tokenizer.Tokenize();

            t1.Stop();

            foreach (var token in tokens)
                Console.WriteLine(token);

            Console.WriteLine($"Compiled in {t1.ElapsedMilliseconds}ms");
        }
    }

    internal sealed class RuntimeConfig
    {
        public string EntrySourceFile { get; init; }

        private RuntimeConfig()
        {
            // NOTE Private ctor to enforce factory pattern
        }

        public static RuntimeConfig? FromCommandLine(string[] args)
        {
            if (args.Length < 1)
                return null;

            return new RuntimeConfig
            {
                EntrySourceFile = args[0]
            };
        }

        public static void PrintHelp()
        {
            Console.WriteLine("Cobalt <SourceFile>");
        }
    }
}