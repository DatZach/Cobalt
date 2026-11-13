using Compiler.Server.Services;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;

namespace Compiler.Server.Handlers
{
    internal sealed class CompletionHandler : CompletionHandlerBase
    {
        private readonly CompilerService compilerService;
        private readonly ILanguageServerConfiguration config;
        private readonly ILanguageServerFacade facade;

        public CompletionHandler(
            CompilerService compilerService,
            ILanguageServerConfiguration config,
            ILanguageServerFacade facade
        )
        {
            this.compilerService = compilerService;
            this.config = config;
            this.facade = facade;
        }

        public override Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new CompletionList(
                new CompletionItem
                {
                    Label = "add",
                    Kind = CompletionItemKind.Function,
                    Detail = "add(int x, int y) int\n---\nSome description here",
                    InsertText = "add",
                    InsertTextFormat = InsertTextFormat.PlainText,
                    InsertTextMode = InsertTextMode.AdjustIndentation
                }
            ));
        }

        public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken)
        {
            return Task.FromResult(request);
        }

        protected override CompletionRegistrationOptions CreateRegistrationOptions(
            CompletionCapability capability,
            ClientCapabilities clientCapabilities
        ) {
            return new CompletionRegistrationOptions
            {
                DocumentSelector = new TextDocumentSelector(
                    new TextDocumentFilter { Pattern = CobLanguageServer.GlobPattern }
                ),
                TriggerCharacters = new Container<string>("(", ",", ".", "=", " ")
            };
        }
    }
}
