using Compiler.Server.Debug.Services;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Compiler.Server.Debug.Handlers
{
    internal sealed class SetVariableHandler : SetVariableHandlerBase
    {
        private readonly DebuggerService debuggerService;

        public SetVariableHandler(DebuggerService debuggerService)
        {
            this.debuggerService = debuggerService;
        }

        public override Task<SetVariableResponse> Handle(SetVariableArguments request, CancellationToken cancellationToken)
        {
            var value = debuggerService.Evaluate(request.Value);
            var variable = debuggerService.Evaluate(request.Name);
            variable.Value = value.Value;

            return Task.FromResult(new SetVariableResponse
            {
                Value = variable.Value.ToString(),
                Type = variable.Type.ToString()
            });
        }
    }

    internal sealed class SetExpressionHandler : SetExpressionHandlerBase
    {
        public override Task<SetExpressionResponse> Handle(SetExpressionArguments request, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
            return Task.FromResult(new SetExpressionResponse
            {

            });
        }
    }
}