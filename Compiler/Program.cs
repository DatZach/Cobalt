using System.Diagnostics;
using Compiler.Ast;
using Compiler.CodeGeneration;
using Compiler.CodeGeneration.Artifacts;
using Compiler.Interpreter;
using Compiler.Lexer;
using Compiler.Server;
using Nerdbank.Streams;

namespace Compiler
{
    public static class Program
    {
        internal static RuntimeConfig Config { get; private set; }

        internal static string SourceDirectory { get; private set; }

        internal static string LibraryDirectory { get; private set; }

        internal static string PreambleSource { get; private set; }

        private static Stopwatch swCompiler;
        private static Stopwatch swRuntime;

        public static async Task Main(string[] args)
        {
            Config = RuntimeConfig.FromCommandLine(args);
            if (Config == null)
            {
                RuntimeConfig.PrintHelp();
                return;
            }

            EstablishEnvironment();

            switch (Config.Mode)
            {
                case eRunMode.CompileAndRun:
                    DoCompileAndRun();
                    break;

                case eRunMode.LanguageServer:
                    await DoLanguageServer();
                    break;

                case eRunMode.REPL:
                    DoREPL();
                    break;
            }
        }

        private static void EstablishEnvironment()
        {
            var asm = typeof(Program).Assembly;
            SourceDirectory = Path.GetDirectoryName(Config.EntrySourceFilePath);
            LibraryDirectory = Config.LibraryDirectory ?? Path.Combine(Path.GetDirectoryName(asm.Location), "Library");

            PreambleSource = Config.PreambleSource ?? $"import Library;{Environment.NewLine}{Environment.NewLine}";
        }

        private static void DoCompileAndRun()
        {
            var messages = new MessageCollection();
            Artifact? artifact;

            swCompiler = Stopwatch.StartNew();
            try
            {
                var source = PreambleSource + FileSystem.ReadAllText(Config.EntrySourceFilePath);
                var tokens = Tokenizer.Tokenize(source, Config.EntrySourceFilePath, messages);
                var ast = Parser.Parse(tokens, messages);
                artifact = CodeGeneration.Compiler.Compile(ast, messages);
            }
            catch (FatalParserException ex)
            {
                messages.Add(Message.UncaughtException0, Token.EndOfStream, Token.EndOfStream, ex.Message);
                artifact = null;
            }
            catch (Exception ex)
            {
                messages.Add(Message.UncaughtException1, Token.EndOfStream, Token.EndOfStream, ex.Message, ex.StackTrace);
                artifact = null;
            }
            finally
            {
                swCompiler.Stop();
            }

            if (artifact == null)
            {
                messages.Print();
                return;
            }
            else if (messages.Count > 0)
            {
                messages.Print();

                if (messages.HasErrors)
                    return;
            }

            ArtifactFactory.Assemble(artifact);

            PrintCompilerState(artifact);
            PrintCompilerStatistics();
            
            if (artifact.ArtifactDirectives.Count == 0)
            {
                swRuntime = Stopwatch.StartNew();
                using var vm = new VirtualMachine(artifact);
                if (artifact.EntryFunction == null)
                {
                    Console.WriteLine("Aborting. No artifact specified, and no Main function exported.");
                    return;
                }

                try
                {
                    vm.ExecuteFunction(artifact.EntryFunction);
                }
                catch 
                {
                    /* Squash */
                }

                swRuntime.Stop();

                PrintRuntimeStatistics();
            }
        }

        private static async Task DoLanguageServer()
        {
            Console.SetOut(TextWriter.Null);

            var iStream = Console.OpenStandardInput();
            var oStream = Console.OpenStandardOutput();

            var server = new CobLanguageServer(
                iStream.UsePipeReader(),
                oStream.UsePipeWriter()
            );

            await server.Initialize();
            await server.WaitForExitAsync();
        }

        private static void DoREPL()
        {

        }

        private static void PrintCompilerState(Artifact artifact)
        {
            if (!Config.AssemblyVerboseOutput)
                return;

            Console.WriteLine("Artifacts");
            foreach (var directive in artifact.ArtifactDirectives)
                Console.WriteLine($"\t{directive}");
            Console.WriteLine();

            Console.WriteLine("Imports");
            foreach (var import in artifact.Imports)
                Console.WriteLine($"\t{import.Library} {import.SymbolName}");
            Console.WriteLine();

            Console.WriteLine("Exports");
            foreach (var export in artifact.Exports)
                Console.WriteLine($"\t{export}");
            Console.WriteLine();

            Console.WriteLine("Functions");
            for (var i = 0; i < artifact.Functions.Count; ++i)
            {
                var function = artifact.Functions[i];
                Console.WriteLine($"\t{i}\t{function.FullyQualifiedName}");
            }

            Console.WriteLine();

            Console.WriteLine("Globals");
            for (var i = 0; i < artifact.Globals.Count; i++)
            {
                var global = artifact.Globals[i];
                Console.WriteLine($"\t{i}\t{global}");
            }

            Console.WriteLine();

            Console.WriteLine("Modules");
            foreach (var module in artifact.Modules)
                Console.WriteLine($"\t{module.Name}");

            Console.WriteLine("Functions");
            for (var j = 0; j < artifact.Functions.Count; j++)
            {
                var function = artifact.Functions[j];
                if (function == artifact.EntryFunction)
                    continue;

                Console.WriteLine($"f{j}\t{function.FullyQualifiedName} -> {function.ReturnType}; .locals = {function.Locals.Count}; .cconv = {function.CallingConvention}");
                if (function.Body.Instructions.Count == 0)
                    Console.WriteLine("\t\t(EMPTY)");
                else for (var i = 0; i < function.Body.Instructions.Count; ++i)
                {
                    var inst = function.Body.Instructions[i];
                    foreach (var label in function.Body.Labels.Where(x => x.Location == i))
                        Console.WriteLine($"\t{label}:");

                    Console.WriteLine($"{i}\t\t{inst}");
                }

                Console.WriteLine();
            }

            Console.WriteLine();

            Console.WriteLine("Entry Point Function");
            if (artifact.EntryFunction == null)
                Console.WriteLine("(NOT DECLARED)");
            else
            {
                var function = artifact.EntryFunction;
                Console.WriteLine($"\t{function.FullyQualifiedName} -> {function.ReturnType}; .locals = {function.Locals.Count}; .cconv = {function.CallingConvention}");
                for (var i = 0; i < function.Body.Instructions.Count; ++i)
                {
                    var inst = function.Body.Instructions[i];
                    foreach (var label in function.Body.Labels.Where(x => x.Location == i))
                        Console.WriteLine($"\t{label}:");

                    Console.WriteLine($"{i}\t\t{inst}");
                }
            }

            Console.WriteLine();
        }

        private static void PrintCompilerStatistics()
        {
            if (Config.StatisticsVerboseOutputLevel <= 0)
                return;

            Console.WriteLine($"Compiled in {swCompiler.ElapsedMilliseconds}ms");
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

        private static void PrintRuntimeStatistics()
        {
            if (Config.StatisticsVerboseOutputLevel > 1)
            {
                Console.WriteLine($"Runtime complete in {swRuntime.ElapsedMilliseconds}ms");
            }
        }
    }

    internal sealed class RuntimeConfig
    {
        public eRunMode Mode { get; init; }

        public string? EntrySourceFilePath { get; init; }

        public string? LibraryDirectory { get; init; }

        public string? PreambleSource { get; init; }

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
            eRunMode mode;
            if (args.Length == 0)
                mode = eRunMode.REPL;
            else if (OptionalArgument("--server", false))
                mode = eRunMode.LanguageServer;
            else if (args.Length >= 1 && !args[0].StartsWith("--"))
                mode = eRunMode.CompileAndRun;
            else
                return null;

            return new RuntimeConfig
            {
                Mode = mode,
                EntrySourceFilePath = args.FirstOrDefault(),
                LibraryDirectory = OptionalArgument<string>("--library-directory"),
                PreambleSource = OptionalArgument<string>("--preamble"),
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
            Console.WriteLine("Cobalt [SourceFile] [--server]");
        }
    }

    internal enum eRunMode
    {
        None,
        CompileAndRun,
        LanguageServer,
        REPL
    }
}