using Compiler.CodeGeneration;
using Compiler.CodeGeneration.Artifacts;
using Compiler.Server.Language.Services;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;

namespace Compiler.Server.Language.Handlers
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
            var items = new List<CompletionItem>();

            var artifact = compilerService.Artifact;
            if (artifact != null)
            {
                // TODO Resolve ctx at cursor position
                var ctx = artifact.EntryFunction as IScopeContext;
                while (ctx != null)
                {
                    if (ctx is IDebugSymbolProvider provider)
                    {
                        items.AddRange(provider.Symbols.Select(x => new CompletionItem
                        {
                            Label = x.Name!,
                            Kind = x switch
                            {
                                Variable y => y.Mutable ? CompletionItemKind.Variable : CompletionItemKind.Constant,
                                Function => CompletionItemKind.Function,
                                RecordType => CompletionItemKind.Class,
                                Module => CompletionItemKind.Module,
                                _ => CompletionItemKind.Unit
                            },
                            Detail = x.ToString(),
                            InsertText = x.Name,
                            InsertTextFormat = InsertTextFormat.PlainText,
                            InsertTextMode = InsertTextMode.AdjustIndentation
                        }));
                    }

                    ctx = ctx.Parent;
                }
            }

            return Task.FromResult(new CompletionList(items));
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
