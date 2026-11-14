using Compiler.Ast;
using Compiler.CodeGeneration.Artifacts;
using Compiler.Lexer;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using MessageType = Compiler.Ast.MessageType;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Compiler.Server.Services
{
    internal sealed class CompilerService
    {
        private ILanguageServer? languageServer;

        public void Initialize(ILanguageServer languageServer)
        {
            this.languageServer = languageServer;
        }

        public void OpenDocument(DocumentUri uri, string content)
        {
            var path = uri.GetFileSystemPath();

            FileSystem.OpenDocument(path, content);

            ParseSingle(uri);
        }

        public void CloseDocument(DocumentUri uri)
        {
            var path = uri.GetFileSystemPath();
            FileSystem.CloseDocument(path);
        }

        public void ChangeDocument(DocumentUri uri, Container<TextDocumentContentChangeEvent> changes)
        {
            var path = uri.GetFileSystemPath();
            foreach (var change in changes)
                FileSystem.ChangeDocument(path, change.Text, change.Range);

            ParseSingle(uri);
        }

        private void ParseSingle(DocumentUri uri)
        {
            var path = uri.GetFileSystemPath();

            var messages = new MessageCollection();
            Artifact? artifact;

            try
            {
                var source = Program.PreambleSource + FileSystem.ReadAllText(path);
                var tokens = Tokenizer.Tokenize(source, path, messages);
                var ast = Parser.Parse(tokens, messages);
                artifact = CodeGeneration.Compiler.Compile(ast, messages);
            }
            catch (FatalParserException ex)
            {
                messages.Add(Message.UncaughtException0, Token.EndOfStream, Token.EndOfStream, ex.Message);
                artifact = null;
            }
            catch (Exception ex)
            {
                messages.Add(Message.UncaughtException1, Token.EndOfStream, Token.EndOfStream, ex.Message, ex.StackTrace);
                artifact = null;
            }

            DispatchDiagnosticsFromMessage(messages);
        }

        private void DispatchDiagnosticsFromMessage(MessageCollection messages)
        {
            var allDiagnostics = new Dictionary<string, List<Diagnostic>>();
            foreach (var message in messages)
            {
                if (!allDiagnostics.TryGetValue(message.StartToken.Filename, out var diagnostics))
                {
                    diagnostics = new List<Diagnostic>();
                    allDiagnostics[message.StartToken.Filename] = diagnostics;
                }

                var startLine = message.StartToken.Line;
                var startColumn = message.StartToken.Column;
                int endLine, endColumn;
                if (message.EndToken == null)
                {
                    endLine = startLine;
                    endColumn = startColumn + message.StartToken.Value.Length;
                }
                else
                {
                    endLine = message.EndToken.Line;
                    endColumn = message.EndToken.Column + message.EndToken.Value.Length;
                }

                // HACK TODO Not always true...
                //if (message.StartToken.Filename == Program.Config.EntrySourceFilePath)
                if (true)
                {
                    int preambleLineCount = Program.PreambleSource.Count(x => x == '\n');
                    startLine -= preambleLineCount - 1;
                    endLine -= preambleLineCount - 1;
                }

                startLine = Math.Max(startLine, 1);
                startColumn = Math.Max(endColumn, 1);
                endLine = Math.Max(endLine, 1);
                endColumn = Math.Max(endColumn, 1);

                diagnostics.Add(new Diagnostic
                {
                    Code = message.Definition.Id,
                    Message = message.Content,
                    Severity = message.Definition.Type switch
                    {
                        MessageType.Error => DiagnosticSeverity.Error,
                        MessageType.Warning => DiagnosticSeverity.Warning,
                        MessageType.Suggestion => DiagnosticSeverity.Hint,
                        _ => throw new ArgumentOutOfRangeException()
                    },
                    Range = new Range(startLine - 1, startColumn - 1, endLine - 1, endColumn - 1)
                });
            }

            foreach (var x in allDiagnostics)
            {
                languageServer?.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
                {
                    Uri = new DocumentUri("file", null, x.Key, null, null),
                    Diagnostics = x.Value
                });
            }
        }
    }
}
