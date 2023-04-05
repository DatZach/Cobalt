using System.Text;
using Compiler.Lexer;
using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Ast.Visitors;

namespace Compiler.CodeGeneration
{
    internal sealed class X86Compiler : IExpressionVisitor<int>
    {
        public MachineCodeBuffer Buffer { get; }

        private Function CurrentFunction => fstack.Peek();

        private readonly Stack<Function> fstack;
        private readonly List<Function> functions;
        private readonly List<object> rdata;
        private readonly List<SymbolImport> idata;

        public X86Compiler()
        {
            Buffer = new MachineCodeBuffer();

            fstack = new Stack<Function>();
            functions = new List<Function>();
            rdata = new List<object>();
            idata = new List<SymbolImport>();
        }

        public int Visit(ScriptExpression expression)
        {
            idata.Add(new SymbolImport
            {
                Library = "kernel32",
                SymbolName = "ExitProcess"
            });

            var expressions = expression.Expressions;
            for (int i = 0; i < expressions.Count; ++i)
                expressions[i].Accept(this);

            // Preamble
            Buffer.Emit(@"
                format PE console
                entry start
                use32

                include 'include/win32a.inc'

                section '.text' code executable
                start:
                    call " + functions[0].Name + @"
                    push 0
                    call [ExitProcess]

            ");

            // Text
            foreach (var f in functions)
            {
                Buffer.Emit(f.Name + ":");
                Buffer.Emit("push ebp");
                Buffer.Emit("mov ebp, esp");
                Buffer.Emit($"sub esp, {f.Locals.Count * 4}");
                for (var i = 0; i < f.ClobberedRegisters.Count; i++)
                {
                    var reg = f.ClobberedRegisters[i];
                    Buffer.Emit($"push {reg}");
                }

                Buffer.Emit(f.Body.ToString());

                for (var i = f.ClobberedRegisters.Count - 1; i >= 0; --i)
                {
                    var reg = f.ClobberedRegisters[i];
                    Buffer.Emit($"pop {reg}");
                }

                Buffer.Emit("leave");
                Buffer.Emit("ret");
            }

            // Readonly Data
            Buffer.Emit("section '.rdata' data readable");
            for (var i = 0; i < rdata.Count; i++)
            {
                var item = rdata[i];
                string value;

                if (item is string s)
                    value = $"'{s}', 0";
                else
                    throw new Exception("Unknown rdata type");

                Buffer.Emit($"rdata_{i} db {value}");
            }

            // Imports
            Buffer.Emit("section '.idata' data readable import");

            var libraries = idata.Select(x => x.Library).Distinct().ToList();
            for (var i = 0; i < libraries.Count; i++)
            {
                var library = libraries[i];
                string line = "";

                if (i == 0) line += "library ";
                else line += "        ";

                line += library;
                line += ", '";
                line += library;
                line += ".dll'";

                if (i != libraries.Count - 1)
                    line += ", \\";

                Buffer.Emit(line);
            }

            foreach (var f in idata)
            {
                Buffer.Emit($"import {f.Library}, {f.SymbolName}, '{f.SymbolName}'");
            }

            return 0;
        }

        public int Visit(VarExpression expression)
        {
            foreach (var decl in expression.Declarations)
            {
                decl.Initializer?.Accept(this);

                if (fstack.Count > 0)
                {
                    var lIndex = CurrentFunction.Locals.Count;
                    CurrentFunction.Locals.Add(decl.Name);
                    var reg = CurrentFunction.FreeRegister();
                    CurrentFunction.Body.Emit($"mov dword [ebp - {(lIndex + 1) * 4}], {reg}");
                }
            }

            return 0;
        }

        public int Visit(ExternExpression expression)
        {
            idata.Add(new SymbolImport
            {
                Library = expression.Library,
                SymbolName = expression.SymbolName
            });

            return 0;
        }

        public int Visit(FunctionExpression expression)
        {
            var function = new Function(expression.Name);
            fstack.Push(function);

            expression.Body?.Accept(this);

            fstack.Pop();
            functions.Add(function);

            return 0;
        }

        public int Visit(BinaryOperatorExpression expression)
        {
            expression.Left.Accept(this);
            expression.Right.Accept(this);

            switch (expression.Operator)
            {
                case TokenType.Add:
                {
                    var b = CurrentFunction.FreeRegister();
                    var a = CurrentFunction.FreeRegister();
                    CurrentFunction.Body.Emit($"add {a}, {b}");
                    var reg = CurrentFunction.AllocateRegister();
                    CurrentFunction.Body.Emit($"mov {reg}, {a}");
                    break;
                }

                case TokenType.Subtract:
                {
                    var b = CurrentFunction.FreeRegister();
                    var a = CurrentFunction.FreeRegister();
                    CurrentFunction.Body.Emit($"sub {a}, {b}");
                    var reg = CurrentFunction.AllocateRegister();
                    CurrentFunction.Body.Emit($"mov {reg}, {a}");
                    break;
                }

                case TokenType.Multiply:
                {
                    var b = CurrentFunction.FreeRegister();
                    var a = CurrentFunction.FreeRegister();
                    CurrentFunction.Body.Emit($"imul {a}, {b}"); // TODO unsigned version
                    var reg = CurrentFunction.AllocateRegister();
                    CurrentFunction.Body.Emit($"mov {reg}, {a}");
                    break;
                }

                case TokenType.Divide:
                {
                    var b = CurrentFunction.FreeRegister();
                    var a = CurrentFunction.FreeRegister();
                    var eax = CurrentFunction.PreserveRegister("eax");
                    var edx = CurrentFunction.PreserveRegister("edx");
                    var ebx = CurrentFunction.PreserveRegister("ebx");
                    CurrentFunction.Body.Emit($"mov ebx, {b}");
                    CurrentFunction.Body.Emit($"mov eax, {a}");
                    CurrentFunction.Body.Emit("cdq");
                    CurrentFunction.Body.Emit($"idiv ebx");
                    var reg = CurrentFunction.AllocateRegister();
                    CurrentFunction.Body.Emit($"mov {reg}, eax");
                    CurrentFunction.RestoreRegister(ebx);
                    CurrentFunction.RestoreRegister(edx);
                    CurrentFunction.RestoreRegister(eax);
                    break;
                }

                case TokenType.Modulo:
                {
                    var b = CurrentFunction.FreeRegister();
                    var a = CurrentFunction.FreeRegister();
                    var eax = CurrentFunction.PreserveRegister("eax");
                    var edx = CurrentFunction.PreserveRegister("edx");
                    var ebx = CurrentFunction.PreserveRegister("ebx");
                    CurrentFunction.Body.Emit($"mov ebx, {b}");
                    CurrentFunction.Body.Emit($"mov eax, {a}");
                    CurrentFunction.Body.Emit("cdq");
                    CurrentFunction.Body.Emit($"idiv ebx");
                    var reg = CurrentFunction.AllocateRegister();
                    CurrentFunction.Body.Emit($"mov {reg}, edx");
                    CurrentFunction.RestoreRegister(ebx);
                    CurrentFunction.RestoreRegister(edx);
                    CurrentFunction.RestoreRegister(eax);
                    break;
                }

                case TokenType.BitLeftShift:
                {
                    var b = CurrentFunction.FreeRegister();
                    var a = CurrentFunction.FreeRegister();
                    var ecx = CurrentFunction.PreserveRegister("ecx");
                    CurrentFunction.Body.Emit($"mov ecx, {b}");
                    CurrentFunction.Body.Emit($"shl {a}, cl");
                    var reg = CurrentFunction.AllocateRegister();
                    CurrentFunction.Body.Emit($"mov {reg}, {a}");
                    CurrentFunction.RestoreRegister(ecx);
                    break;
                }

                case TokenType.BitRightShift:
                {
                    var b = CurrentFunction.FreeRegister();
                    var a = CurrentFunction.FreeRegister();
                    var ecx = CurrentFunction.PreserveRegister("ecx");
                    CurrentFunction.Body.Emit($"mov ecx, {b}");
                    CurrentFunction.Body.Emit($"shr {a}, cl");
                    var reg = CurrentFunction.AllocateRegister();
                    CurrentFunction.Body.Emit($"mov {reg}, {a}");
                    CurrentFunction.RestoreRegister(ecx);
                    break;
                }

                case TokenType.BitAnd:
                {
                    var b = CurrentFunction.FreeRegister();
                    var a = CurrentFunction.FreeRegister();
                    CurrentFunction.Body.Emit($"and {a}, {b}");
                    var reg = CurrentFunction.AllocateRegister();
                    CurrentFunction.Body.Emit($"mov {reg}, {a}");
                    break;
                }

                case TokenType.BitOr:
                {
                    var b = CurrentFunction.FreeRegister();
                    var a = CurrentFunction.FreeRegister();
                    CurrentFunction.Body.Emit($"or {a}, {b}");
                    var reg = CurrentFunction.AllocateRegister();
                    CurrentFunction.Body.Emit($"mov {reg}, {a}");
                    break;
                }

                case TokenType.BitXor:
                {
                    var b = CurrentFunction.FreeRegister();
                    var a = CurrentFunction.FreeRegister();
                    CurrentFunction.Body.Emit($"xor {a}, {b}");
                    var reg = CurrentFunction.AllocateRegister();
                    CurrentFunction.Body.Emit($"mov {reg}, {a}");
                    break;
                }

                default:
                    throw new NotImplementedException();
            }

            return 0;
        }

        public int Visit(BlockExpression expression)
        {
            for (var i = 0; i < expression.Expressions.Count; i++)
            {
                var expr = expression.Expressions[i];
                expr.Accept(this);
            }

            return 0;
        }

        public int Visit(CallExpression expression)
        {
            var args = expression.Arguments;
            for (int i = args.Count - 1; i >= 0; --i)
            {
                args[i].Accept(this);
                var reg = CurrentFunction.FreeRegister();
                CurrentFunction.Body.Emit($"push {reg}");
            }

            CurrentFunction.Body.Emit($"call [{expression.FunctionExpression.Token.Value}]");

            return 0;
        }

        public int Visit(IdentifierExpression expression)
        {
            var idx = CurrentFunction.Locals.IndexOf(expression.Value);
            var reg = CurrentFunction.AllocateRegister();
            CurrentFunction.Body.Emit($"mov {reg}, dword [ebp - {(idx + 1) * 4}]");
            
            return 0;
        }

        public int Visit(NumberExpression expression)
        {
            var reg = CurrentFunction.AllocateRegister();
            CurrentFunction.Body.Emit($"mov {reg}, {expression.LongValue}");

            return 0;
        }

        public int Visit(StringExpression expression)
        {
            var rdIndex = rdata.Count;
            rdata.Add(expression.Value);

            var reg = CurrentFunction.AllocateRegister();
            CurrentFunction.Body.Emit($"mov {reg}, rdata_{rdIndex}");

            return 0;
        }

        public int Visit(EmptyExpression expression)
        {
            return 0;
        }

        private sealed record SymbolImport
        {
            public string Library { get; init; }

            public string SymbolName { get; init;  }
        }

        private sealed class Function
        {
            public string Name { get; }

            public List<string> Locals { get; }

            public List<string> ClobberedRegisters { get; }

            public MachineCodeBuffer Body { get; }

            private int registersInUse;

            public Function(string name)
            {
                Name = name;
                Locals = new List<string>();
                ClobberedRegisters = new List<string>();
                Body = new MachineCodeBuffer();
            }

            private readonly static string[] registers = { "eax", "edx", "ecx", "ebx" };

            public string AllocateRegister()
            {
                var reg = registers[registersInUse++];
                if (!ClobberedRegisters.Contains(reg))
                    ClobberedRegisters.Add(reg);

                return reg;
            }

            public string FreeRegister()
            {
                --registersInUse;

                return registers[registersInUse];
            }

            public string? PreserveRegister(string registerName)
            {
                var idx = Array.IndexOf(registers, registerName);
                if (idx < registersInUse)
                {
                    Body.Emit($"push {registerName}");
                    return registerName;
                }

                return null;
            }

            public void RestoreRegister(string? registerName)
            {
                if (registerName == null)
                    return;

                Body.Emit($"pop {registerName}");
            }
        }
    }

    internal sealed class MachineCodeBuffer
    {
        private readonly StringBuilder builder;

        public MachineCodeBuffer()
        {
            builder = new StringBuilder(4096);
        }

        public void Emit(string op)
        {
            builder.AppendLine(op);
        }

        public override string ToString()
        {
            return builder.ToString();
        }
    }
}
