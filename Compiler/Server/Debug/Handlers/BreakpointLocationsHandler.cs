using Compiler.Server.Debug.Services;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Compiler.Server.Debug.Handlers
{
    internal sealed class BreakpointLocationsHandler : BreakpointLocationsHandlerBase
    {
        private readonly DebuggerService debuggerService;

        public BreakpointLocationsHandler(DebuggerService debuggerService)
        {
            this.debuggerService = debuggerService;
        }

        public override Task<BreakpointLocationsResponse> Handle(BreakpointLocationsArguments request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new BreakpointLocationsResponse
            {
                Breakpoints = new Container<BreakpointLocation>(
                    debuggerService.ResolveBreakpoints().Select(x => new BreakpointLocation
                    {
                        Line = x.Line.Value,
                        Column = x.Column,
                        EndLine = x.EndLine,
                        EndColumn = x.EndColumn
                    })
                )
            });
        }
    }

    internal sealed class SetBreakpointsHandler : SetBreakpointsHandlerBase
    {
        private readonly DebuggerService debuggerService;

        public SetBreakpointsHandler(DebuggerService debuggerService)
        {
            this.debuggerService = debuggerService;
        }

        public override Task<SetBreakpointsResponse> Handle(SetBreakpointsArguments request, CancellationToken cancellationToken)
        {
            if (request.Source.Path != null && request.Breakpoints != null)
            {
                debuggerService.UpdateBreakpoints(
                    request.Source.Path,
                    request.Breakpoints
                );
            }

            return Task.FromResult(new SetBreakpointsResponse
            {
                Breakpoints = new Container<Breakpoint>(
                    debuggerService.ResolveBreakpoints()
                )
            });
        }
    }
}