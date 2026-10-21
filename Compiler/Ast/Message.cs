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
        public void Add(Message.Def definition, Expression expression, params object?[] args) => AddInner(definition, expression.StartToken, expression.EndToken, string.Format(definition.Content, args.Select(x => x ?? "(null)")));
        public void Add(Message.Def definition, Expression expression, object? arg0) => AddInner(definition, expression.StartToken, expression.EndToken, string.Format(definition.Content, arg0 ?? "(null)"));
        public void Add(Message.Def definition, Expression expression, object? arg0, object? arg1) => AddInner(definition, expression.StartToken, expression.EndToken, string.Format(definition.Content, arg0 ?? "(null)", arg1 ?? "(null)"));
        public void Add(Message.Def definition, Expression expression, object? arg0, object? arg1, object? arg2) => AddInner(definition, expression.StartToken, expression.EndToken, string.Format(definition.Content, arg0 ?? "(null)", arg1 ?? "(null)", arg2 ?? "(null)"));

        public void Add(Message.Def definition, Token startToken) => AddInner(definition, startToken, null, definition.Content);

        public void Add(Message.Def definition, Token startToken, params object?[] args) => AddInner(definition, startToken, null, string.Format(definition.Content, args.Select(x => x ?? "(null)")));
        public void Add(Message.Def definition, Token startToken, object? arg0) => AddInner(definition, startToken, null, string.Format(definition.Content, arg0 ?? "(null)"));
        public void Add(Message.Def definition, Token startToken, object? arg0, object? arg1) => AddInner(definition, startToken, null, string.Format(definition.Content, arg0 ?? "(null)", arg1 ?? "(null)"));
        public void Add(Message.Def definition, Token startToken, object? arg0, object? arg1, object? arg2) => AddInner(definition, startToken, null, string.Format(definition.Content, arg0 ?? "(null)", arg1 ?? "(null)", arg2 ?? "(null)"));

        public void Add(Message.Def definition, Token startToken, Token? endToken, object? arg0) => AddInner(definition, startToken, endToken, string.Format(definition.Content, arg0 ?? "(null)"));
        public void Add(Message.Def definition, Token startToken, Token? endToken, object? arg0, object? arg1) => AddInner(definition, startToken, endToken, string.Format(definition.Content, arg0 ?? "(null)", arg1 ?? "(null)"));
        public void Add(Message.Def definition, Token startToken, Token? endToken, object? arg0, object? arg1, object? arg2) => AddInner(definition, startToken, endToken, string.Format(definition.Content, arg0 ?? "(null)", arg1 ?? "(null)", arg2 ?? "(null)"));
        public void Add(Message.Def definition, Token startToken, Token? endToken, params object?[] args) => AddInner(definition, startToken, endToken, string.Format(definition.Content, args.Select(x => x ?? "(null)")));

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
            int preambleLineCount = Program.PreambleSource.Count(x => x == '\n');

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

                var contentLines = message.Content.Split('\n');
                for (var i = 0; i < contentLines.Length; ++i)
                {
                    var contentLine = contentLines[i];

                    Console.ForegroundColor = color;
                    Console.Write(typePrefix);
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write(i == 0 ? " " : "    ");
                    Console.WriteLine(contentLine);
                }

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
                    Console.Write($"{{0,{indentCount}}} | ", i - preambleLineCount + 1);
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine(line);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write($"{indent} ' ");
                    Console.Write(new string(' ' , startToken.Column));
                    Console.ForegroundColor = color;
                    var tokenLength = startToken.Value.Length;
                    if (startToken != endToken && startToken.Line == endToken.Line)
                        tokenLength += endToken.Column - (startToken.Column + startToken.Value.Length) + endToken.Value.Length;
                    if (startToken.Type == TokenType.String) tokenLength += 2;
                    Console.WriteLine(new string('^', Math.Max(1, Math.Min(tokenLength, line.Length - startToken.Column))));
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
        public static Def UncaughtException { get; } = new(MessageType.Error, "Fatal: {0}\n{1}");

        public static Def UnexpectedToken0 { get; } = new(MessageType.Error, "Unexpected token");

        public static Def UnexpectedToken1 { get; } = new(MessageType.Error, "Unexpected {0}");

        public static Def UnexpectedToken2 { get; } = new(MessageType.Error, "Expected {0} but found {1} instead");

        public static Def StringUnterminated { get; } = new(MessageType.Error, "Untermined string");

        public static Def StringIllegalEscape { get; } = new(MessageType.Error, "Invalid escape code");

        public static Def CharacterIllegalLength { get; } = new(MessageType.Error, "Illegal length for specified type suffix");

        public static Def CharacterIllegalTypeSuffix { get; } = new(MessageType.Error, "Characters type suffix must be u|s8,16,32");

        public static Def MissingClosingBrace { get; } = new(MessageType.Error, "Missing closing brace for this block");

        public static Def ExcessiveSpreadParameters { get; } = new(MessageType.Error, "Cannot define multiple spread parameters");

        public static Def IllegalTypeName { get; } = new(MessageType.Error, "Illegal typename '{0}'");

        public static Def IllegalNumber { get; } = new(MessageType.Error, "Illegal number '{0}'");

        public static Def ReturnTypeMismatch { get; } = new(MessageType.Error, "Return value of type '{0}' does not match expected '{1}'");

        public static Def AotCannotUseVoid { get; } = new(MessageType.Error, "Cannot evaluate ahead-of-time expression on void");

        public static Def CannotIndexType { get; } = new(MessageType.Error, "Cannot index type '{0}'");

        public static Def CannotResolveThis { get; } = new(MessageType.Error, "Unable to resolve to 'this' symbol for thiscall function");

        public static Def NoMatchingFunctionCandidate { get; } = new(MessageType.Error, "No candidate matches signature: {0}. Considered:\n{1}");

        public static Def CannotErrorCoalesceType { get; } = new(MessageType.Error, "Cannot error coalesce type '{0}'");

        public static Def CannotNilCoalesceType { get; } = new(MessageType.Error, "Cannot nil coalesce type '{0}'");

        public static Def CannotInstantiateType { get; } = new(MessageType.Error, "Cannot instantiate type '{0}'");

        public static Def FunctionParameterCountMismatch { get; } = new(MessageType.Error, "Expected {0} parameters, but received {1} instead");

        public static Def UndeclaredIdentifier { get; } = new(MessageType.Error, "Undeclared identifier '{0}'");

        public static Def TypeMismatch { get; } = new(MessageType.Error, "Expected type '{0}' but recieved uncastable '{1}' instead");

        public static Def IllegalAssignmentImmutable { get; } = new(MessageType.Error, "Cannot assign to a 'const' identifier.");

        public static Def IllegalAssignmentTypeMismatch { get; } = new(MessageType.Error, "Cannot assign value of type '{0}' to identifier of type '{1}'");

        public static Def IllegalAssignment { get; } = new(MessageType.Error, "Cannot assign value to immutable symbol");

        public static Def CannotRedeclareEntryPoint { get; } = new(MessageType.Error, "Multiple Entry Points may not be declared");

        public static Def CannotRedeclareVariable { get; } = new(MessageType.Error, "Cannot redeclare variable '{0}'");

        public static Def MissingFunctionBody { get; } = new(MessageType.Error, "Missing function body");

        public static Def CannotAccessPrivateSymbol { get; } = new(MessageType.Error, "Cannot access private symbol '{0}' from module '{1}'");

        public static Def SymbolConflictsWithOther { get; } = new(MessageType.Error, "The Symbol '{0}' conflicts with a previous defined symbol of the same name");

        public static Def MalformedVarDeclaration { get; } = new(MessageType.Error, "Variable declarations must specify a type, or an inline assignment, or both");

        public static Def CannotFindImport { get; } = new(MessageType.Error, "Unable to find any module which matches '{0}'");

        public static Def CannotContinue { get; } = new(MessageType.Error, "No loop context to continue from");

        public static Def CannotBreak { get; } = new(MessageType.Error, "No loop context to break from");

        public static Def CannotBreakNoLabel { get; } = new(MessageType.Error, "No loop label '{0}' declared in scope to break from");

        public static Def CannotNakedNestStatement { get; } = new(MessageType.Error, "Statements may not be nested in naked blocks.");

        public static Def CannotDeclareSymbolHere { get; } = new(MessageType.Error, "Cannot declare a symbol of this type here");

        public static Def IllegalCallingConvention { get; } = new(MessageType.Error, "The calling convention '{0}' is not legal here");

        public static Def IllegalPattern { get; } = new(MessageType.Error, "Illegal pattern");

        public static Def MissingPattern { get; } = new(MessageType.Error, "Missing pattern for '{0}'");

        public static Def CannotEnumerateType { get; } = new(MessageType.Error, "Cannot enumerate type '{0}'");

        public static Def CannotMatchNotPattern { get; } = new(MessageType.Error, "Cannot pattern match 'is not' against multiple patterns");

        public static Def CannotDereferenceContext { get; } = new(MessageType.Error, "Cannot '.' dereference '{0}' context");

        public static Def IllegalOperationBetweenTypes { get; } = new(MessageType.Error, "Cannot perform {0} operation between types '{1}' and '{2}'");

        public static Def FixedArrayDimensionMismatch { get; } = new(MessageType.Error, "Fixed Array Literal expects {0} elements to be initialized but only {1} may be");

        public static Def CannotSpreadNumberHere { get; } = new(MessageType.Error, "Cannot spread number literal here");

        public static Def ExpectedTypeName { get; } = new(MessageType.Error, "Expected type name");

        public static Def CannotCastType { get; } = new(MessageType.Error, "Cannot cast type '{0}' to '{1}'");

        public static Def IllegalNarrowingConstraints { get; } = new(MessageType.Error, "Cannot satisfy narrowing constraints");

        public static Def RefinementMustResolveToBoolean { get; } = new(MessageType.Error, "Refinment expression must result in a boolean");

        public static Def RefinementViolated { get; } = new(MessageType.Error, "Refinement is violated");

        public static Def IllegalEnumRefinementSuperType { get; } = new(MessageType.Error, "Enum Refinement illegal for super-type '{0}'; expected int, uint, or string");

        public static Def ValueOverflow { get; } = new(MessageType.Error, "Value would overflow type");

        public static Def CannotMixinNonTraitType { get; } = new(MessageType.Error, "Cannot 'mixin' non-trait record type '{0}'");

        public static Def TraitNotFullyImplemented { get; } = new(MessageType.Error, "Trait '{0}' is not fully implemented");

        public static Def NotAllFieldsInitialized { get; } = new(MessageType.Error, "Not all fields assigned to by constructor and initializer");

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
