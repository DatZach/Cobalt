using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;
using Thread = OmniSharp.Extensions.DebugAdapter.Protocol.Models.Thread;

namespace Compiler.Server.Debug.Handlers
{
    internal sealed class ThreadsHandler : ThreadsHandlerBase
    {
        public override Task<ThreadsResponse> Handle(ThreadsArguments request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ThreadsResponse
            {
                Threads = new Container<Thread>(
                    new Thread { Id = 0, Name = "main" }
                )
            });
        }
    }
}