using Compiler.Server.Debug.Services;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Compiler.Server.Debug.Handlers
{
    internal sealed class StackTraceHandler : StackTraceHandlerBase
    {
        private readonly DebuggerService debuggerService;

        public StackTraceHandler(DebuggerService debuggerService)
        {
            this.debuggerService = debuggerService;
        }

        public override Task<StackTraceResponse> Handle(StackTraceArguments request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new StackTraceResponse
            {
                StackFrames = new Container<StackFrame>(debuggerService.ResolveStackTrace())
            });
        }
    }
}