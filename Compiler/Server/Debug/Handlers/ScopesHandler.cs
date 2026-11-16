using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Compiler.Server.Debug.Handlers
{
    internal sealed class ScopesHandler : ScopesHandlerBase
    {
        public override Task<ScopesResponse> Handle(ScopesArguments request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ScopesResponse
            {
                Scopes = new Container<Scope>(
                    new Scope
                    {
                        Name = "Locals",
                        Source = new Source { Path = "C:\\Temp\\CobaltPlayground\\corn.cob" },
                        VariablesReference = 3
                    }
                )
            });
        }
    }
}