using System.Diagnostics;
using Compiler.Ast;
using Compiler.CodeGeneration;
using Compiler.CodeGeneration.Platform;
using Compiler.Interpreter;
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

            var t2 = Stopwatch.StartNew();
            var source = File.ReadAllText(Config.EntrySourceFile); // TODO Abstract to track file io better
            t2.Stop();

            var t3 = Stopwatch.StartNew();
            var tokens = Tokenizer.Tokenize(source);
            t3.Stop();

            var t4 = Stopwatch.StartNew();
            var ast = Parser.Parse(tokens);
            t4.Stop();

            var t5 = Stopwatch.StartNew();
            var compiler = new CodeGeneration.Compiler();
            ast.Accept(compiler);
            t5.Stop();

            if (compiler.Artifacts.Count > 0)
            {
                var t6 = Stopwatch.StartNew();
                ArtifactFactory.Assemble(compiler);
                t6.Stop();

                t1.Stop();

                if (Config.AssemblyVerboseOutput)
                    PrintCompilerState(compiler);

                if (Config.StatisticsVerboseOutputLevel > 0)
                {
                    Console.WriteLine($"Compiled in {t1.ElapsedMilliseconds}ms");
                    if (Config.StatisticsVerboseOutputLevel > 1)
                    {
                        Console.WriteLine($"\tFile IO    {t2.ElapsedMilliseconds}ms");
                        Console.WriteLine($"\tTokenize   {t3.ElapsedMilliseconds}ms");
                        Console.WriteLine($"\tAST        {t4.ElapsedMilliseconds}ms");
                        Console.WriteLine($"\tIM Compile {t5.ElapsedMilliseconds}ms");
                        Console.WriteLine($"\tAssemble   {t6.ElapsedMilliseconds}ms");
                    }
                }
            }
            else
            {
                using var vm = new VirtualMachine(compiler);
                if (!compiler.Exports.TryGetValue("Main", out var mainFunctionName))
                {
                    Console.WriteLine("Aborting. No artifact specified, and no Main function exported.");
                    return;
                }

                var mainFunction = compiler.Functions.FirstOrDefault(x => x.Name == mainFunctionName);
                vm.ExecuteFunction(mainFunction);
            }
        }

        private static void PrintCompilerState(CodeGeneration.Compiler compiler)
        {
            Console.WriteLine(string.Join("\r\n", compiler.Artifacts));
            
            Console.WriteLine("Imports");
            foreach (var import in compiler.Imports)
                Console.WriteLine($"\t{import.Library} {import.SymbolName}");
            Console.WriteLine();

            Console.WriteLine("Exports");
            foreach (var export in compiler.Exports)
                Console.WriteLine($"\t{export}");
            Console.WriteLine();

            Console.WriteLine("Globals");
            foreach (var global in compiler.Globals)
                Console.WriteLine($"\t{global}");
            Console.WriteLine();

            Console.WriteLine("Functions");
            foreach (var function in compiler.Functions)
            {
                Console.WriteLine($"\t{function.Name} -> {function.ReturnType}");
                Console.WriteLine($"\t\t.locals = {function.Locals.Count}");
                Console.WriteLine($"\t\t.cconv = {function.CallingConvention}");
                if (function.Body == null)
                {
                    Console.WriteLine("\tBodyless");
                    continue;
                }

                foreach (var inst in function.Body.Instructions)
                    Console.WriteLine($"\t{inst}");
            }
            Console.WriteLine();
        }
    }

    internal sealed class RuntimeConfig
    {
        public string EntrySourceFile { get; init; }

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
                EntrySourceFile = args[0],
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