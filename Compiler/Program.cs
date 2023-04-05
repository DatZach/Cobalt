using System.Diagnostics;
using Compiler.Ast;
using Compiler.CodeGeneration;
using Compiler.Lexer;

namespace Compiler
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

            var t1 = Stopwatch.StartNew();

            var source = File.ReadAllText(Config.EntrySourceFile);
            var tokens = Tokenizer.Tokenize(source);
            var ast = Parser.Parse(tokens);
            var x86 = new X86Compiler();
            ast.Accept(x86);
            
            var outputFilename = Path.ChangeExtension(Path.GetFileName(Config.EntrySourceFile), "exe");
            X86Assembler.Assemble(x86.Buffer, outputFilename);

            t1.Stop();
            
            Console.WriteLine($"Compiled in {t1.ElapsedMilliseconds}ms");
        }
    }

    internal sealed class RuntimeConfig
    {
        public string EntrySourceFile { get; init; }

        public string? FasmPath { get; init; }

        public bool FasmVerboseOutput { get; init; }

        public bool AstVerboseOutput { get; init; }

        public bool AssemblyVerboseOutput { get; init; }

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
                EntrySourceFile = args[0],
                FasmPath = OptionalArgument<string>("--fasm"),
                FasmVerboseOutput = OptionalArgument("--fasm-verbose", false),
                AstVerboseOutput = OptionalArgument("--ast-verbose", false),
                AssemblyVerboseOutput = OptionalArgument("--asm-verbose", false)
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
            Console.WriteLine("Cobalt <SourceFile>");
        }
    }
}