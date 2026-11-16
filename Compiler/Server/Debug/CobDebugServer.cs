using Compiler.Server.Debug.Handlers;
using Compiler.Server.Debug.Services;
using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;
using OmniSharp.Extensions.DebugAdapter.Protocol.Server;
using OmniSharp.Extensions.DebugAdapter.Server;

namespace Compiler.Server.Debug
{
    internal sealed class CobDebugServer
    {
        public DebugAdapterServer Interface => server;

        private readonly TaskCompletionSource<bool> serverStopped;
        private readonly DebugAdapterServer server;

        public CobDebugServer(Stream inputStream, Stream outputStream)
        {
            serverStopped = new TaskCompletionSource<bool>();

            server = DebugAdapterServer.Create(options =>
            {
                options
                    .WithContentModifiedSupport(false)
                    .WithInput(inputStream)
                    .WithOutput(outputStream)
                    .WithServices(services => services.AddSingleton(_ => new DebuggerService(this)))
                    .WithHandler<LaunchHandler>()
                    .WithHandler<TerminateHandler>()
                    .WithHandler<RestartHandler>()
                    .WithHandler<PauseHandler>()
                    .WithHandler<ContinueHandler>()
                    .WithHandler<NextHandler>()
                    .WithHandler<StepInTargetsHandler>()
                    .WithHandler<StepInHandler>()
                    .WithHandler<StepOutHandler>()
                    .WithHandler<GotoTargetsHandler>()
                    .WithHandler<GotoHandler>()
                    .WithHandler<BreakpointLocationsHandler>()
                    .WithHandler<SetBreakpointsHandler>()
                    .WithHandler<DisassembleHandler>()
                    .WithHandler<EvaluateHandler>()
                    .WithHandler<ScopesHandler>()
                    .WithHandler<ThreadsHandler>()
                    .WithHandler<StackTraceHandler>()
                    .WithHandler<VariablesHandler>()
                    .WithHandler<ExceptionInfoHandler>()
                    .WithHandler<SetExceptionBreakpointsHandler>()
                    .WithHandler<SetVariableHandler>()
                    .WithHandler<SetExpressionHandler>()
                    .WithHandler<ConfigurationDoneHandler>()
                    .OnInitialized(DebugAdapterServer_OnInitialized);
            });
        }

        public Task Initialize()
        {
            return server.Initialize(CancellationToken.None);
        }

        public void Shutdown()
        {
            serverStopped.SetResult(true);
        }

        public async Task WaitForExitAsync()
        {
            await serverStopped.Task.ConfigureAwait(false);
        }

        private Task DebugAdapterServer_OnInitialized(IDebugAdapterServer debugAdapterServer, InitializeRequestArguments initializeRequestArguments, InitializeResponse response, CancellationToken cancellationToken)
        {
            response.ExceptionBreakpointFilters = new Container<ExceptionBreakpointsFilter>(
                new ExceptionBreakpointsFilter { Filter = "$UncaughtErrors", Label = "Uncaught Errors", Default = true }
            );
            return Task.CompletedTask;
        }
    }
}
