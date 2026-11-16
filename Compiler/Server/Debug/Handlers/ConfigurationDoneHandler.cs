using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Compiler.Server.Debug.Handlers
{
    internal sealed class ConfigurationDoneHandler : ConfigurationDoneHandlerBase
    {
        public override Task<ConfigurationDoneResponse> Handle(ConfigurationDoneArguments request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ConfigurationDoneResponse());
        }
    }
}
