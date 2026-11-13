using Compiler.Server.Services;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;

namespace Compiler.Server.Handlers
{
    internal sealed class TextDocumentHandler : TextDocumentSyncHandlerBase
    {
        private readonly CompilerService compilerService;
        private readonly ILanguageServerConfiguration config;
        private readonly ILanguageServerFacade facade;

        public TextDocumentHandler(
            CompilerService compilerService,
            ILanguageServerConfiguration config,
            ILanguageServerFacade facade
        )
        {
            this.compilerService = compilerService;
            this.config = config;
            this.facade = facade;
        }

        public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
        {
            compilerService.OpenDocument(
                request.TextDocument.Uri,
                request.TextDocument.Text
            );

            return Unit.Task;
        }

        public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
        {
            return Unit.Task;
        }

        public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken)
        {
            return Unit.Task;
        }

        public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
        {
            return Unit.Task;
        }

        public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
        {
            return new TextDocumentAttributes(uri, CobLanguageServer.Extension);
        }

        protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
            TextSynchronizationCapability capability,
            ClientCapabilities clientCapabilities
        )
        {
            return new TextDocumentSyncRegistrationOptions
            {
                DocumentSelector = new TextDocumentSelector(
                    new TextDocumentFilter { Pattern = CobLanguageServer.GlobPattern }
                ),
                Change = TextDocumentSyncKind.Incremental,
                Save = new SaveOptions
                {
                    IncludeText = true
                }
            };
        }
    }
}
