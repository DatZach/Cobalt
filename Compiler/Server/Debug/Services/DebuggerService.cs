using System.Text;
using Compiler.Ast;
using Compiler.CodeGeneration.Artifacts;
using Compiler.Interpreter;
using Compiler.Lexer;
using OmniSharp.Extensions.DebugAdapter.Protocol.Events;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using Message = Compiler.Ast.Message;
using Variable = OmniSharp.Extensions.DebugAdapter.Protocol.Models.Variable;

namespace Compiler.Server.Debug.Services
{
    internal class DebuggerService
    {
        private bool? signalIsRunning;
        //private bool isRunning, singleStep;
        private VirtualMachine2? vm;
        private Artifact? artifact;

        private readonly List<CobBreakpoint> breakpoints;

        private readonly CobDebugServer debugServer;

        public DebuggerService(CobDebugServer debugServer)
        {
            this.debugServer = debugServer;

            breakpoints = new List<CobBreakpoint>();
        }

        public void Start()
        {
            //singleStep = false;
            //isRunning = true;
            signalIsRunning = true;

            if (vm == null)
            {
                var messages = new MessageCollection();

                try
                {
                    var source = Program.PreambleSource + FileSystem.ReadAllText(Program.Config.EntrySourceFilePath);
                    var tokens = Tokenizer.Tokenize(source, Program.Config.EntrySourceFilePath, messages);
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

                if (artifact == null || artifact.EntryFunction == null)
                    return;

                vm = new VirtualMachine2(artifact);
                vm.OnExceptionOccurred += VirtualMachine_OnExceptionOccurred;
                vm.Push(artifact.EntryFunction, new List<CodeGeneration.Artifacts.Variable>(), null);

                Task.Factory.StartNew(Task_VirtualMachine);
            }
        }

        private void Task_VirtualMachine()
        {
            //var wasRunning = isRunning;
            var isRunning = signalIsRunning.GetValueOrDefault(false);
            while (vm != null)
            {
                var wasRunning = isRunning;

                var lSignalIsRunning = signalIsRunning;
                if (lSignalIsRunning != null)
                {
                    isRunning = lSignalIsRunning.Value;
                    signalIsRunning = null;
                }

                //if (singleStep)
                //{
                //    var frame = vm.Stack.Peek();
                //    if (frame.Function.Body.Instructions[frame.IP].StartToken != null)
                //        isRunning = false;
                //}
                //else
                if (breakpoints.Count > 0 && wasRunning)
                {
                    var frame = vm.Stack.Peek();
                    for (var i = 0; isRunning && i < breakpoints.Count; ++i)
                    {
                        var breakpoint = breakpoints[i];
                        if (breakpoint.IsTriggered(frame))
                        {
                            if (breakpoint.LogMessage != null)
                                SendOutputLine(OutputEventCategory.Console, breakpoint.LogMessage);
                            else
                                isRunning = false;
                        }
                    }
                }

                if (isRunning != wasRunning)
                {
                    if (isRunning)
                        debugServer.Interface.SendContinued(new ContinuedEvent { AllThreadsContinued = true });
                    else
                        debugServer.Interface.SendStopped(new StoppedEvent { AllThreadsStopped = true });
                }

                if (isRunning)
                    vm.Tick();
            }

            //debugServer.Shutdown();
        }

        public string? LastError_Name { get; private set; }
        public string? LastError_StackTrace { get; private set; }
        public Exception? LastError_NativeException { get; private set; }

        private void VirtualMachine_OnExceptionOccurred(string errorName, string stackTrace, Exception? nativeException)
        {
            LastError_Name = errorName;
            LastError_StackTrace = stackTrace;
            LastError_NativeException = nativeException;
            signalIsRunning = false;

            debugServer.Interface.SendStopped(new StoppedEvent
            {
                Reason = StoppedEventReason.Exception,
                Description = "PAUSED ON ERROR",
                PreserveFocusHint = false,
                Text = errorName,
                AllThreadsStopped = true,
                ThreadId = 0
            });
        }

        public void Stop()
        {
            signalIsRunning = false;
            vm = null;
            debugServer.Shutdown();
        }

        public void Pause()
        {
            signalIsRunning = false;
        }

        public void StepOver()
        {
            //singleStep = true;
            signalIsRunning = true;
        }

        public void StepIn()
        {
            //singleStep = true;
            signalIsRunning = true;
        }

        public void StepOut()
        {
            //singleStep = true;
            signalIsRunning = true;
        }

        // TODO Make this a property?
        public IReadOnlyList<StackFrame> ResolveStackTrace()
        {
            if (vm == null)
                return Array.Empty<StackFrame>();

            var result = new List<StackFrame>();
            foreach (var frame in vm.Stack)
            {
                // TODO Remove Function.Source I think
                if (frame.Function.Source == null)
                    continue;

                frame.Function.ResolveSourceCodeRangeForIP(
                    frame.IP,
                    out var startLine, out var startColumn,
                    out var endLine, out var endColumn
                );
                result.Add(new StackFrame
                {
                    Id = result.Count,
                    Name = frame.Function.Name,
                    Source  = new Source
                    {
                        Name = Path.GetFileNameWithoutExtension(frame.Function.Source.Filename),
                        Path = frame.Function.Source.Filename
                    },
                    Line = startLine,
                    Column = startColumn,
                    EndLine = endLine,
                    EndColumn = endColumn,
                    InstructionPointerReference = frame.IP.ToString(),
                });
            }

            return result;
        }

        public IReadOnlyList<Breakpoint> ResolveBreakpoints()
        {
            return breakpoints.Select(x => new Breakpoint
            {
                Source = new Source { Path = x.Path },
                Line = x.Line,
                Column = x.Column,
                Verified = true
            }).ToList();
        }

        public void UpdateBreakpoints(string source, Container<SourceBreakpoint> newBreakpoints)
        {
            var result = new List<CobBreakpoint>();

            result.AddRange(breakpoints.Where(x => x.Path != source));
            result.AddRange(newBreakpoints.Select(x => new CobBreakpoint(this, source, x.Line, x.Column, x.Condition, x.LogMessage)));

            breakpoints.Clear();
            breakpoints.AddRange(result);
        }

        public IReadOnlyList<Variable> ResolveVariables()
        {
            if (vm == null)
                return Array.Empty<Variable>();

            var result = new List<Variable>();
            var frame = vm.Stack.Peek();

            result.AddRange(frame.Parameters.Where(x => x != null).Select(x => new Variable
            {
                Name = x.Name,
                Value = x.Value?.ToString() ?? "(null)",
                Type = x.Type.ToString(),
            }));

            result.AddRange(frame.Locals.Where(x => x != null).Select(x => new Variable
            {
                Name = frame.Function.Locals[IndexOf(frame.Locals, x)].Name,
                Value = x.Value?.ToString() ?? "(null)",
                Type = x.Type.ToString(),
            }));

            return result;

            int IndexOf(IReadOnlyList<CodeGeneration.Artifacts.Variable> list, CodeGeneration.Artifacts.Variable value)
            {
                for (int i = 0; i < list.Count; ++i)
                {
                    if (list[i] == value)
                        return i;
                }

                return -1;
            }
        }

        public CodeGeneration.Artifacts.Variable? Evaluate(string expression)
        {
            // TODO Support frameID
            if (vm == null)
                return null;

            if (long.TryParse(expression, out var longValue))
                return new CodeGeneration.Artifacts.Variable("$imm", CobType.Int, longValue);

            int idx;
            var frame = vm.Stack.Peek();
            if ((idx = IndexOf(frame.Function.Parameters, expression)) != -1)
                return frame.Parameters[idx];

            if ((idx = IndexOf(frame.Function.Locals, expression)) != -1)
                return frame.Locals[idx];

            return null;

            int IndexOf(IReadOnlyList<CodeGeneration.Artifacts.Variable> list, string name)
            {
                for (int i = 0; i < list.Count; ++i)
                {
                    if (list[i].Name == name)
                        return i;
                }

                return -1;
            }
        }

        public IReadOnlyList<DisassembledInstruction> Disassemble()
        {
            if (vm == null)
                return Array.Empty<DisassembledInstruction>();

            int line = -1;

            var frame = vm.Stack.Peek();
            var result = new List<DisassembledInstruction>();
            for (var i = 0; i < frame.Function.Body.Instructions.Count; ++i)
            {
                var instruction = frame.Function.Body.Instructions[i];
                
                if (instruction.StartToken != null)
                    line = instruction.StartToken.Line;

                result.Add(new DisassembledInstruction
                {
                    Address = i.ToString("G"),
                    Instruction = (instruction.StartToken != null ? "# " : "  ") + instruction.ToString(),
                    Location = new Source { Path = frame.Function.Source.Filename },
                    Line = line
                });
            }

            return result;
        }

        public void GoTo(long id)
        {
            if (id == 0)
                vm.Stack.Peek().IP = 0;
            else if (id == 1)
                vm.Stack.Peek().IP = 50;
        }

        private void SendOutputLine(OutputEventCategory category, string value) => SendOutput(category, value + Environment.NewLine);
        private void SendOutput(OutputEventCategory category, string value)
        {
            var output = category == OutputEventCategory.Console
                ? EvaluateInterpolation(value)
                : value;
            debugServer.Interface.SendOutput(new OutputEvent
            {
                Category = category,
                Output = output
            });
        }

        private string EvaluateInterpolation(string value)
        {
            var sb = new StringBuilder(value.Length);

            int i = value.IndexOf('{'), j = 0;
            for (; i != -1; i = value.IndexOf('{', i + 1))
            {
                sb.Append(value.Substring(j, i - j));
                var k = value.IndexOf('}', i + 1);
                if (k == -1) break;
                var expression = value.Substring(i + 1, k - i - 1);
                var result = Evaluate(expression);
                sb.Append(result?.Value ?? "(null)");

                j = k + 1;
                i = k;
            }

            sb.Append(value.Substring(j, value.Length - j));

            return sb.ToString();
        }

        private sealed record CobBreakpoint(
           DebuggerService Debugger,
            string Path,
            int Line,
            int? Column,
            string? Condition,
            string? LogMessage
        )
        {
            public bool IsTriggered(VirtualMachine2.Frame frame)
            {
                var preambleLines = 1; // TODO Implement, obviously

                var token = frame.Function.Body.Instructions[frame.IP].StartToken;
                if (token == null)
                    return false;

                if (!Path.Equals(token.Filename, StringComparison.OrdinalIgnoreCase)
                    ||  Line != token.Line - preambleLines)
                    return false;

                if (Condition != null)
                    return Debugger.Evaluate(Condition)?.Value is 1;

                return true;
            }
        }
    }
}
