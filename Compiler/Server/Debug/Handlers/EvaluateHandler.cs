using Compiler.Server.Debug.Services;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Compiler.Server.Debug.Handlers
{
    internal sealed class EvaluateHandler : EvaluateHandlerBase
    {
        private readonly DebuggerService debuggerService;

        public EvaluateHandler(DebuggerService debuggerService)
        {
            this.debuggerService = debuggerService;
        }

        public override Task<EvaluateResponse> Handle(EvaluateArguments request, CancellationToken cancellationToken)
        {
            var result = debuggerService.Evaluate(request.Expression);
            return Task.FromResult(new EvaluateResponse
            {
                Result = result?.Value?.ToString() ?? "(null)",
                Type = result?.Type.ToString(),
                VariablesReference = -1
            });
        }
    }
}