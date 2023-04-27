using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Compiler.Ast.Expressions;
using Compiler.Lexer;

namespace Compiler.Ast
{
    [DebuggerDisplay("Count = {Count}")]
    internal sealed class MessageCollection : IList<Message>
    {
        public bool HasErrors { get; private set; }

        public int Count => messages.Count;

        public bool IsReadOnly => false;

        public Message this[int index]
        {
            get => messages[index];
            set => messages[index] = value;
        }

        private readonly List<Message> messages;
        private readonly List<MessageDirective> directives;

        public MessageCollection()
        {
            messages = new List<Message>();
            directives = new List<MessageDirective>();
        }

        public void Add(Message.Def definition, Expression expression) => AddInner(definition, expression.StartToken, expression.EndToken, definition.Content);
        public void Add(Message.Def definition, Expression expression, params object[] args) => AddInner(definition, expression.StartToken, expression.EndToken, string.Format(definition.Content, args));
        public void Add(Message.Def definition, Expression expression, object? arg0) => AddInner(definition, expression.StartToken, expression.EndToken, string.Format(definition.Content, arg0));
        public void Add(Message.Def definition, Expression expression, object? arg0, object? arg1) => AddInner(definition, expression.StartToken, expression.EndToken, string.Format(definition.Content, arg0, arg1));
        public void Add(Message.Def definition, Expression expression, object? arg0, object? arg1, object? arg2) => AddInner(definition, expression.StartToken, expression.EndToken, string.Format(definition.Content, arg0, arg1, arg2));

        public void Add(Message.Def definition, Token startToken) => AddInner(definition, startToken, null, definition.Content);

        public void Add(Message.Def definition, Token startToken, params object[] args) => AddInner(definition, startToken, null, string.Format(definition.Content, args));
        public void Add(Message.Def definition, Token startToken, object? arg0) => AddInner(definition, startToken, null, string.Format(definition.Content, arg0));
        public void Add(Message.Def definition, Token startToken, object? arg0, object? arg1) => AddInner(definition, startToken, null, string.Format(definition.Content, arg0, arg1));
        public void Add(Message.Def definition, Token startToken, object? arg0, object? arg1, object? arg2) => AddInner(definition, startToken, null, string.Format(definition.Content, arg0, arg1, arg2));

        public void Add(Message.Def definition, Token startToken, Token? endToken, object? arg0) => AddInner(definition, startToken, endToken, string.Format(definition.Content, arg0));
        public void Add(Message.Def definition, Token startToken, Token? endToken, object? arg0, object? arg1) => AddInner(definition, startToken, endToken, string.Format(definition.Content, arg0, arg1));
        public void Add(Message.Def definition, Token startToken, Token? endToken, object? arg0, object? arg1, object? arg2) => AddInner(definition, startToken, endToken, string.Format(definition.Content, arg0, arg1, arg2));
        public void Add(Message.Def definition, Token startToken, Token? endToken, params object[] args) => AddInner(definition, startToken, endToken, string.Format(definition.Content, args));

        void ICollection<Message>.Add(Message item) => messages.Add(item);

        private void AddInner(Message.Def definition, Token startToken, Token? endToken, string content)
        {
            if (IsMessageProhibited(definition, startToken))
                return;

            messages.Add(new Message(definition, startToken, endToken, content));
            HasErrors = HasErrors || definition.Type == MessageType.Error;
        }

        public void AddDirective(string id, int line, MessageDirectiveType type) => directives.Add(new MessageDirective(id, line, type));

        public void Clear() => messages.Clear();

        public bool Contains(Message item) => messages.Contains(item);

        public void CopyTo(Message[] array, int arrayIndex) => messages.CopyTo(array, arrayIndex);

        public bool Remove(Message item) => messages.Remove(item);

        public int IndexOf(Message item) => messages.IndexOf(item);

        public void Insert(int index, Message item) => messages.Insert(index, item);

        public void RemoveAt(int index) => messages.RemoveAt(index);

        private bool IsMessageProhibited(Message.Def definition, Token token)
        {
            if (definition.Type == MessageType.Ignore)
                return true;
            
            int line = token.Line;
            for (var i = 0; i < directives.Count; ++i)
            {
                var directive = directives[i];
                if (line < directive.Line)
                    break;

                if (directive.Id != definition.Id && directive.Id != "all")
                    continue;

                switch (directive.Type)
                {
                    case MessageDirectiveType.DisableOnce:
                        if (line == directive.Line)
                            return true;
                        break;

                    case MessageDirectiveType.Disable:
                        return true;

                    case MessageDirectiveType.Restore:
                        return false;
                }
            }
            
            return false;
        }

        public IEnumerator<Message> GetEnumerator() => messages.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Print()
        {
            int errorCount = 0;
            int warnCount = 0;
            int suggestionCount = 0;

            messages.Sort((a, b) =>
            {
                var cmp = string.CompareOrdinal(a.StartToken.Filename, b.StartToken.Filename);
                if (cmp != 0)
                    return cmp;

                return a.StartToken.Line - b.StartToken.Line;
            });

            foreach (var message in messages)
            {
                var startToken = message.StartToken;
                var endToken = message.EndToken ?? startToken;

                string typePrefix;
                ConsoleColor color;
                if (message.Definition.Type == MessageType.Error)
                {
                    color = ConsoleColor.Red;
                    typePrefix = "ERR";
                    ++errorCount;
                }
                else if (message.Definition.Type == MessageType.Warning)
                {
                    color = ConsoleColor.Yellow;
                    typePrefix = "WRN";
                    ++warnCount;
                }
                else if (message.Definition.Type == MessageType.Suggestion)
                {
                    color = ConsoleColor.Blue;
                    typePrefix = "SUG";
                    ++suggestionCount;
                }
                else
                    continue;

                Console.ForegroundColor = color;
                Console.Write(typePrefix);
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(" ");
                Console.WriteLine(message.Content);

                var indentCount = (endToken.Line + 1).ToString("G").Length;
                var indent = new string(' ', indentCount);
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(new string('>', indentCount + 2));
                Console.Write(' ');
                //Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine(startToken.Filename);

                for (int i = startToken.Line; i <= endToken.Line; ++i)
                {
                    var line = FileSystem.GetLine(startToken.Filename, startToken.Line) ?? "(missing line)";

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write($"{{0,{indentCount}}} | ", i + 1);
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine(line);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write($"{indent} ' ");
                    Console.Write(new string(' ' , startToken.Column));
                    Console.ForegroundColor = color;
                    var tokenLength = startToken.Value.Length;
                    if (startToken.Type == TokenType.String) tokenLength += 2;
                    Console.WriteLine(new string('^', Math.Min(tokenLength, line.Length - startToken.Column)));
                    Console.WriteLine();
                }
            }

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write("Compilation failed, aborting (");
            Console.Write(errorCount);
            Console.ForegroundColor = ConsoleColor.Red; Console.Write(errorCount == 1 ? " error" : " errors");
            Console.ForegroundColor = ConsoleColor.Gray; Console.Write(", ");
            Console.Write(warnCount);
            Console.ForegroundColor = ConsoleColor.Yellow; Console.Write(warnCount == 1 ? " warning" : " warnings");
            Console.ForegroundColor = ConsoleColor.Gray; Console.WriteLine(")");
            Console.WriteLine();
        }
    }

    internal sealed record Message(
        Message.Def Definition,
        Token StartToken,
        Token? EndToken,
        string Content
    )
    {
        public static Def UnexpectedToken0 { get; } = new(MessageType.Error, "Unexpected token");

        public static Def UnexpectedToken1 { get; } = new(MessageType.Error, "Unexpected {0}");

        public static Def UnexpectedToken2 { get; } = new(MessageType.Error, "Expected {0} but found {1} instead");

        public static Def StringUnterminated { get; } = new(MessageType.Error, "Untermined string");

        public static Def StringIllegalEscape { get; } = new(MessageType.Error, "Invalid escape code");

        public static Def MissingClosingBrace { get; } = new(MessageType.Error, "Missing closing brace for this block");

        public static Def ExcessiveSpreadParameters { get; } = new(MessageType.Error, "Cannot define multiple spread parameters");

        public static Def IllegalTypeName { get; } = new(MessageType.Error, "Illegal typename '{0}'");

        public static Def IllegalNumber { get; } = new(MessageType.Error, "Illegal number '{0}'");

        public static Def ReturnTypeMismatch { get; } = new(MessageType.Error, "Return value of type '{0}' does not match expected '{1}'");

        public static Def AotCannotUseVoid { get; } = new(MessageType.Error, "Cannot evaluate ahead-of-time expression on void");

        public static Def CannotCallType { get; } = new(MessageType.Error, "Cannot call type '{0}'");

        public static Def FunctionParameterCountMismatch { get; } = new(MessageType.Error, "Expected {0} parameters, but received {1} instead");

        public static Def UndeclaredIdentifier { get; } = new(MessageType.Error, "Undeclared identifier '{0}'");

        public static Def ParameterTypeMismatch { get; } = new(MessageType.Error, "Expected type '{0}' but recieved '{1}' instead");

        public sealed record Def(
            MessageType Type,
            string Content,
            [CallerMemberName] string Id = null!
        );
    }

    internal enum MessageType
    {
        Ignore,
        Error,
        Warning,
        Suggestion
    }

    internal sealed record MessageDirective(
        string Id,
        int Line,
        MessageDirectiveType Type
    );

    internal enum MessageDirectiveType
    {
        None,
        Disable,
        Restore,
        DisableOnce
    }
}
