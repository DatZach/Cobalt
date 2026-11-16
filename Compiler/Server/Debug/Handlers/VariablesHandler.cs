using Compiler.Server.Debug.Services;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Compiler.Server.Debug.Handlers
{
    internal sealed class VariablesHandler : VariablesHandlerBase
    {
        private readonly DebuggerService debuggerService;

        public VariablesHandler(DebuggerService debuggerService)
        {
            this.debuggerService = debuggerService;
        }

        public override Task<VariablesResponse> Handle(VariablesArguments request, CancellationToken cancellationToken)
        {
            // TODO Support request
            return Task.FromResult(new VariablesResponse
            {
                Variables = new Container<Variable>(
                    debuggerService.ResolveVariables()
                )
            });
        }
    }
}