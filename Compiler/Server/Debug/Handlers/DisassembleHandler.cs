using Compiler.Server.Debug.Services;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Compiler.Server.Debug.Handlers
{
    internal sealed class DisassembleHandler : DisassembleHandlerBase
    {
        private readonly DebuggerService debuggerService;

        public DisassembleHandler(DebuggerService debuggerService)
        {
            this.debuggerService = debuggerService;
        }

        public override Task<DisassembleResponse> Handle(DisassembleArguments request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new DisassembleResponse
            {
                Instructions = new Container<DisassembledInstruction>(
                    debuggerService.Disassemble()
                )
            });
        }
    }
}