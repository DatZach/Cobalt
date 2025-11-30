using System.Diagnostics;
using Compiler.Ast;
using Compiler.CodeGeneration;
using Compiler.Interpreter;
using Compiler.Lexer;

namespace Compiler
{
    public static class Program
    {
        internal static RuntimeConfig Config { get; private set; }

        internal static string SourceDirectory { get; private set; }

        internal static string LibraryDirectory { get; private set; }

        internal static string PreambleSource { get; private set; }

        private static Stopwatch stopwatch;

        public static void Main(string[] args)
        {
            Config = RuntimeConfig.FromCommandLine(args);
            if (Config == null)
            {
                RuntimeConfig.PrintHelp();
                return;
            }

            EstablishEnvironment();

            stopwatch = Stopwatch.StartNew();

            var messages = new MessageCollection();
            var source = PreambleSource + FileSystem.ReadAllText(Config.EntrySourceFilePath);
            var tokens = Tokenizer.Tokenize(source, Config.EntrySourceFilePath, messages);
            var ast = Parser.Parse(tokens, messages);
            var compiler = CodeGeneration.Compiler.Compile(ast, messages);

            if (messages.Count > 0)
            {
                messages.Print();

                if (messages.HasErrors)
                    return;
            }

            ArtifactFactory.Assemble(compiler);

            stopwatch.Stop();

            PrintCompilerState(compiler);
            PrintCompilerStatistics();
            
            if (compiler.Artifacts.Count == 0)
            {
                using var vm = new VirtualMachine(compiler);
                if (compiler.EntryFunction == null)
                {
                    Console.WriteLine("Aborting. No artifact specified, and no Main function exported.");
                    return;
                }

                vm.ExecuteFunction(compiler.EntryFunction);
            }
        }

        private static void EstablishEnvironment()
        {
            var asm = typeof(Program).Assembly;
            SourceDirectory = Path.GetDirectoryName(Config.EntrySourceFilePath);
            LibraryDirectory = Config.LibraryDirectory ?? Path.Combine(Path.GetDirectoryName(asm.Location), "Library");

            PreambleSource = $"import Library;{Environment.NewLine}{Environment.NewLine}";
        }

        private static void PrintCompilerState(CodeGeneration.Compiler compiler)
        {
            if (!Config.AssemblyVerboseOutput)
                return;

            Console.WriteLine("Artifacts");
            foreach (var artifact in compiler.Artifacts)
                Console.WriteLine($"\t{artifact}");
            Console.WriteLine();

            Console.WriteLine("Imports");
            foreach (var import in compiler.Imports)
                Console.WriteLine($"\t{import.Library} {import.SymbolName}");
            Console.WriteLine();

            Console.WriteLine("Exports");
            foreach (var export in compiler.Exports)
                Console.WriteLine($"\t{export}");
            Console.WriteLine();

            Console.WriteLine("Globals");
            for (var i = 0; i < compiler.Globals.Count; i++)
            {
                var global = compiler.Globals[i];
                Console.WriteLine($"\t{i}\t{global}");
            }

            Console.WriteLine();

            Console.WriteLine("Modules");
            foreach (var module in compiler.Modules)
            {
                Console.WriteLine($"\t{module.Name ?? "(root)"}");
                Console.WriteLine("\tFunctions");
                foreach (var function in module.Functions)
                {
                    Console.WriteLine($"\t\t{function.Name} -> {function.ReturnType}; .locals = {function.Locals.Count}; .cconv = {function.CallingConvention}");
                    //Console.WriteLine($"\t\t\t");
                    //Console.WriteLine($"\t\t\t.cconv = {function.CallingConvention}");
                    if (function.Body == null)
                    {
                        Console.WriteLine("\t\tBodyless");
                        continue;
                    }

                    for (var i = 0; i < function.Body.Instructions.Count; ++i)
                    {
                        var inst = function.Body.Instructions[i];
                        // TODO Optimize to Dictionary before?
                        var label = function.Body.Labels.FirstOrDefault(x => x.Location == i);
                        if (label != null)
                            Console.WriteLine($"\t\t.{function.FullyQualifiedName}_{label.Index}:");

                        Console.WriteLine($"\t\t\t{inst}");
                    }
                }
            }

            Console.WriteLine();
        }

        private static void PrintCompilerStatistics()
        {
            if (Config.StatisticsVerboseOutputLevel <= 0)
                return;

            Console.WriteLine($"Compiled in {stopwatch.ElapsedMilliseconds}ms");
            if (Config.StatisticsVerboseOutputLevel > 1)
            {
                Console.WriteLine($"\tFile IO    {FileSystem.TotalMilliseconds}ms");
                Console.WriteLine($"\tTokenize   {Tokenizer.TotalMilliseconds}ms");
                Console.WriteLine($"\tAST        {Parser.TotalMilliseconds}ms");
                Console.WriteLine($"\tIM Compile {CodeGeneration.Compiler.TotalMilliseconds}ms");
                Console.WriteLine($"\tAssemble   {ArtifactFactory.TotalMilliseconds}ms");
            }

            Console.WriteLine();
        }
    }

    internal sealed class RuntimeConfig
    {
        public string EntrySourceFilePath { get; init; }

        public string? LibraryDirectory { get; init; }

        public string? FasmPath { get; init; }

        public bool FasmVerboseOutput { get; init; }

        public bool AstVerboseOutput { get; init; }

        public bool AssemblyVerboseOutput { get; init; }

        public int StatisticsVerboseOutputLevel { get; init; }

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
                EntrySourceFilePath = args[0],
                LibraryDirectory = OptionalArgument<string>("--library-directory"),
                FasmPath = OptionalArgument<string>("--fasm"),
                FasmVerboseOutput = OptionalArgument("--fasm-verbose", false),
                AstVerboseOutput = OptionalArgument("--ast-verbose", false),
                AssemblyVerboseOutput = OptionalArgument("--asm-verbose", false),
                StatisticsVerboseOutputLevel = OptionalArgument("--stats-verbose", 0)
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