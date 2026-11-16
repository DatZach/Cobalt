using System.IO.Pipelines;
using Compiler.Server.Language.Handlers;
using Compiler.Server.Language.Services;
using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Server;

namespace Compiler.Server.Language
{
    internal sealed class CobLanguageServer
    {
        public const string Extension = "cob";
        public const string GlobPattern = $"**/*.{Extension}";

        public ILanguageServer Interface => server;

        private readonly LanguageServer server;

        public CobLanguageServer(PipeReader inputPipe, PipeWriter outputPipe)
        {
            server = LanguageServer.Create(options =>
            {
                options
                    .WithContentModifiedSupport(false)
                    .WithInput(inputPipe)
                    .WithOutput(outputPipe)
                    .WithServices(services => services.AddSingleton(_ => new CompilerService(this)))
                    .WithHandler<TextDocumentHandler>()
                    .WithHandler<CompletionHandler>();
            });
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
