using Compiler.Ast;
using Compiler.CodeGeneration;
using Compiler.CodeGeneration.Artifacts;
using Compiler.Lexer;
using OmniSharp.Extensions.LanguageServer.Protocol;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
            FileSystem.ClearCache(path);
            FileSystem.ReadAllText(path);

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

            var allDiagnostics = new Dictionary<string, List<Diagnostic>>();
            foreach (var x in messages)
            {
                if (!allDiagnostics.TryGetValue(x.StartToken.Filename, out var diagnostics))
                {
                    diagnostics = new List<Diagnostic>();
                    allDiagnostics[x.StartToken.Filename] = diagnostics;
                }

                var startLine = x.StartToken.Line;
                var startColumn = x.StartToken.Column;
                int endLine, endColumn;
                if (x.EndToken == null)
                {
                    endLine = startLine;
                    endColumn = startColumn + x.StartToken.Value.Length;
                }
                else
                {
                    endLine = x.EndToken.Line;
                    endColumn = x.EndToken.Column + x.EndToken.Value.Length;
                }

                startLine = Math.Max(startLine, 1);
                startColumn = Math.Max(endColumn, 1);
                endLine = Math.Max(endLine, 1);
                endColumn = Math.Max(endColumn, 1);

                diagnostics.Add(new Diagnostic
                {
                    Code = x.Definition.Id,
                    Message = x.Content,
                    Severity = x.Definition.Type switch
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
