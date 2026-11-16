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
        private bool isRunning, singleStep;
        private VirtualMachine2? vm;
        private Artifact? artifact;

        private readonly CobDebugServer debugServer;

        public DebuggerService(CobDebugServer debugServer)
        {
            this.debugServer = debugServer;

            breakpoints = new List<Breakpoint>();
        }

        public void Start()
        {
            singleStep = false;
            isRunning = true;

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

        public string? LastError_Name { get; private set; }
        public string? LastError_StackTrace { get; private set; }
        public Exception? LastError_NativeException { get; private set; }

        private void VirtualMachine_OnExceptionOccurred(string errorName, string stackTrace, Exception? nativeException)
        {
            LastError_Name = errorName;
            LastError_StackTrace = stackTrace;
            LastError_NativeException = nativeException;
            isRunning = false;

            debugServer.Interface.SendStopped(new StoppedEvent
            {
                Reason = StoppedEventReason.Exception,
                Description = "PAUSED ON ERROR",
                PreserveFocusHint = false,
                Text = errorName,
                AllThreadsStopped = true,
                ThreadId = 0
            });
            debugServer.Interface.SendBreakpoint(new BreakpointEvent
            {
                Breakpoint = new Breakpoint
                {

                }
            });
        }

        private void Task_VirtualMachine()
        {
            var wasRunning = isRunning;
            while (vm != null)
            {
                if (isRunning != wasRunning)
                {
                    if (isRunning)
                        debugServer.Interface.SendContinued(new ContinuedEvent { AllThreadsContinued = true });
                    else
                        debugServer.Interface.SendStopped(new StoppedEvent { AllThreadsStopped = true });
                }
                if (!isRunning)
                {
                    wasRunning = false;
                    continue;
                }
                else
                    wasRunning = true;

                wasRunning = isRunning;
                vm.Tick();

                if (singleStep)
                {
                    var frame = vm.Stack.Peek();
                    if (frame.Function.Body.Instructions[frame.IP].Line != -1)
                        isRunning = false;
                }
                else if (breakpoints.Count > 0)
                {
                    var frame = vm.Stack.Peek();
                    foreach (var breakpoint in breakpoints)
                    {
                        if (breakpoint.Source.Path.Equals(frame.Function.Source?.Filename, StringComparison.OrdinalIgnoreCase)
                        &&  breakpoint.Line.Value == frame.Function.ResolveLineFromIP(frame.IP))
                            isRunning = false;
                    }
                }
            }

            //debugServer.Shutdown();
        }

        public void Stop()
        {
            isRunning = false;
            vm = null;
            debugServer.Shutdown();
        }

        public void Pause()
        {
            isRunning = false;
        }

        public void StepOver()
        {
            singleStep = true;
            isRunning = true;
        }

        public void StepIn()
        {
            singleStep = true;
            isRunning = true;
        }

        public void StepOut()
        {
            singleStep = true;
            isRunning = true;
        }

        // TODO Make this a property?
        public IReadOnlyList<StackFrame> ResolveStackTrace()
        {
            if (vm == null)
                return Array.Empty<StackFrame>();

            return vm.Stack.Where(x => x.Function.Source != null).Select((x, i) => new StackFrame
            {
                Id = i,
                Name = x.Function.Name,
                Source  = new Source
                {
                    Name = Path.GetFileNameWithoutExtension(x.Function.Source.Filename),
                    Path = x.Function.Source.Filename
                },
                Line = x.Function.ResolveLineFromIP(x.IP),
                Column = x.Function.Source.Column,
                InstructionPointerReference = x.IP.ToString(),
            }).ToList();
        }

        private List<Breakpoint> breakpoints; // TODO Breakpoint doesn't actually store all the important information

        public IReadOnlyList<Breakpoint> ResolveBreakpoints()
        {
            return breakpoints;
        }

        public void UpdateBreakpoints(string source, Container<SourceBreakpoint> newBreakpoints)
        {
            // TODO Condition
            // TODO LogMessage

            var result = new List<Breakpoint>();
            foreach (var x in breakpoints)
            {
                if (x.Source?.Path == source)
                    continue;

                result.Add(x);
            }

            foreach (var x in newBreakpoints)
            {
                result.Add(new Breakpoint
                {
                    Source = new Source { Path = source },
                    Line = x.Line,
                    Column = x.Column,
                    Verified = true
                });
            }
            breakpoints = result;
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
            if (artifact == null || artifact.EntryFunction == null)
                return Array.Empty<DisassembledInstruction>();

            return artifact.EntryFunction.Body.Instructions.Select((x, i) => new DisassembledInstruction
            {
                Address = i.ToString("G"),
                Instruction = x.ToString(),
                Location = new Source
                {
                    Path = artifact.EntryFunction.Source.Filename
                },
                Line = artifact.EntryFunction.ResolveLineFromIP(i)
            }).ToList();
        }

        public void GoTo(long id)
        {
            if (id == 0)
                vm.Stack.Peek().IP = 0;
            else if (id == 1)
                vm.Stack.Peek().IP = 50;
        }
    }
}
