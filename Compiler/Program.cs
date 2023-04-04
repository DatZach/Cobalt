using System.Diagnostics;
using Compiler.Ast;
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
            var parser = new Parser.Parser(tokens);
            var ast = parser.ParseScript();
            var x86 = new X86Compiler();
            ast.Accept(x86);

            var asm = x86.Buffer.ToString();

            var tmpFilename = Path.Combine(Path.GetTempPath(), Path.GetTempFileName());
            File.WriteAllText(tmpFilename, asm);

            var process = new Process();
            process.StartInfo.FileName = @"C:\Tools\fasmw17330\fasm.exe";
            process.StartInfo.Arguments = $"\"{tmpFilename}\"";
            process.StartInfo.WorkingDirectory = @"C:\Tools\fasmw17330";
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.ErrorDataReceived += Assembler_StdOutRecieved;
            process.EnableRaisingEvents = true;

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            File.Delete(tmpFilename);
            tmpFilename = Path.ChangeExtension(tmpFilename, "exe");
            var dstFilename = Path.ChangeExtension(Path.GetFileName(config.EntrySourceFile), "exe");
            File.Copy(tmpFilename, dstFilename);

            t1.Stop();

            //foreach (var token in tokens)
            //    Console.WriteLine(token);

            // Console.WriteLine(asm);
            Console.WriteLine($"Compiled in {t1.ElapsedMilliseconds}ms");
            
        }

        private static void Assembler_StdOutRecieved(object sender, DataReceivedEventArgs e)
        {
            Console.Write(e.Data);
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