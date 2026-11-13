using System.IO.Pipelines;
using Compiler.Server.Handlers;
using Compiler.Server.Services;
using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Server;

namespace Compiler.Server
{
    internal sealed class CobLanguageServer
    {
        public const string Extension = "cob";
        public const string GlobPattern = $"**/*.{Extension}";

        private readonly LanguageServer server;

        public CobLanguageServer(PipeReader inputPipe, PipeWriter outputPipe)
        {
            server = LanguageServer.Create(options =>
            {
                options
                    .WithContentModifiedSupport(false)
                    .WithInput(inputPipe)
                    .WithOutput(outputPipe)
                    .WithServices(services => services.AddSingleton(_ => new CompilerService()))
                    .WithHandler<TextDocumentHandler>()
                    .WithHandler<CompletionHandler>()
                    .OnInitialized(LanguageServer_OnInitialized);
            });
        }

        private Task LanguageServer_OnInitialized(ILanguageServer languageServer, InitializeParams request, InitializeResult response, CancellationToken cancellationtoken)
        {
            var compilerService = languageServer.GetService<CompilerService>();
            compilerService.Initialize(languageServer);

            return Task.CompletedTask;
        }

        public Task Initialize()
        {
            return server.Initialize(CancellationToken.None);
        }

        public Task WaitForExitAsync()
        {
            return server.WaitForExit;
        }
    }
}
