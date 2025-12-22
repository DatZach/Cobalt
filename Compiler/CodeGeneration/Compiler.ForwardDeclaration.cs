using Compiler.Ast;
using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Ast.Visitors;
using Compiler.Lexer;
using System.Diagnostics;

namespace Compiler.CodeGeneration
{
    internal sealed class ForwardDeclaration : IExpressionVisitor<object>
    {
        public DeclPhase Phase { get; private set; }

        private IContext CurrentContext => contextStack.Peek();

        private Module? currentModule;

        private readonly Compiler compiler;
        private readonly Stack<IContext> contextStack; // TODO IScopedContext or something (Struct, Module, Tuple, etc.)

        public ForwardDeclaration(Compiler compiler)
        {
            this.compiler = compiler;
            contextStack = new Stack<IContext>(4);
        }

        public object Visit(ScriptExpression expression)
        {
            //Debug.Assert(compiler.Modules.Count == 0, "ForwardDeclaration cannot operate on a mutated compiler state");

            currentModule = compiler.FindOrAllocateModule(null);
            contextStack.Push(currentModule);

            for (var phase = DeclPhase.Begin; phase < DeclPhase.Complete; ++phase)
            {
                Phase = phase;

                var expressions = expression.Expressions;
                for (int i = 0; i < expressions.Count; ++i)
                    expressions[i].Accept(this);
            }

            contextStack.Pop();

            return null;
        }

        public object Visit(ModuleExpression expression)
        {
            var prevModule = currentModule;

            if (Phase == DeclPhase.Modules)
                currentModule = compiler.FindOrAllocateModule(expression.Name);
            else if (Phase > DeclPhase.Modules)
                currentModule = compiler.FindModule(expression.Name);
            else
                return null;

            contextStack.Push(currentModule);

            if (expression.Block != null)
            {
                expression.Block.Accept(this);
                currentModule = prevModule;
                contextStack.Pop();
            }

            return null;
        }

        public object Visit(TypeExpression expression)
        {
            if (Phase != DeclPhase.Types)
                return null;

            var typeName = CobType.FromString(expression.TypeName);
            if (!CobType.TryAddAlias(expression.Name, typeName))
                compiler.Messages.Add(Message.SymbolConflictsWithOther, expression.Token, expression.Name);

            return null;
        }

        public object Visit(TupleDefinitionExpression expression)
        {
            if (Phase == DeclPhase.Types)
            {
                // TODO Is this actually a type alias? Shouldn't we resolve these from the TupleTypes field?
                if (!CobType.TryAddAlias(expression.Name, new CobType(eCobType.Tuple, tag: expression)))
                    compiler.Messages.Add(Message.SymbolConflictsWithOther, expression.Token, expression.Name);

                currentModule.TupleTypes.Add(expression);
            }
            else if (Phase > DeclPhase.Types)
            {
                contextStack.Push(expression);
                for (var i = 0; i < expression.Functions.Count; ++i)
                    expression.Functions[i].Accept(this);
                contextStack.Pop();
            }
            
            return null;
        }

        public object Visit(StructDefinitionExpression expression)
        {
            if (Phase == DeclPhase.Types)
            {
                // TODO Is this actually a type alias? Shouldn't we resolve these from the StructTypes field?
                if (!CobType.TryAddAlias(expression.Name, new CobType(eCobType.Struct, tag: expression)))
                    compiler.Messages.Add(Message.SymbolConflictsWithOther, expression.Token, expression.Name);

                currentModule.StructTypes.Add(expression);
            }
            else if (Phase > DeclPhase.Types)
            {
                contextStack.Push(expression);
                for (var i = 0; i < expression.Functions.Count; ++i)
                    expression.Functions[i].Accept(this);
                contextStack.Pop();
            }

            return null;
        }

        public object Visit(VarExpression expression)
        {
            // NOTE We do not dive function bodies in this visitor so any VarExpressions we visit will be
            //      module-level declarations

            if (Phase != DeclPhase.Fields)
                return null;

            foreach (var decl in expression.Declarations)
            {
                var mutable = expression.Type == TokenType.Var;
                var variable = new CobVariable(decl.Name, decl.Type, mutable);
                compiler.AllocateGlobal(variable);
                currentModule.Variables[variable.Name] = variable; // TODO Weird?
            }

            return null;
        }

        public object Visit(ImportExpression expression)
        {
            // X import *
            // X import CobaltSourceFile
            // X import Directory.CobaltSourceFile
            // X import Directory.*
            // X import StandardLibraryCobaltFile
            
            //   import CobaltAssembly *
            //   import CobaltAssembly SpecificIdentifier

            // X import NativeLibrary SpecificIdentifier Type

            // TODO Invoke initializer

            var isNativeImport = expression.SymbolName != null && expression.SymbolTypeSignature != null;
            if (!isNativeImport)
                return VisitImportModule(expression);
            else
                return VisitImportSymbol(expression);

            return null;
        }

        private object VisitImportModule(ImportExpression expression)
        {
            if (Phase != DeclPhase.ModuleImports)
                return null;

            var paths = new List<string>(4);

            var srcDirectory = Path.GetDirectoryName(expression.Token.Filename);
            var libDirectory = Program.LibraryDirectory;

            var sourcePatternName = expression.SourceFile.Replace('.', Path.DirectorySeparatorChar);
                
            var srcSourcePath = Path.Combine(srcDirectory, sourcePatternName + ".cob");
            if (FileSystem.FileExists(srcSourcePath)) paths.Add(srcSourcePath); 
                
            var libSourcePath = Path.Combine(libDirectory, sourcePatternName + ".cob");
            if (FileSystem.FileExists(libSourcePath)) paths.Add(libSourcePath);

            if (sourcePatternName.Contains('*'))
            {
                var sourceDirectory = Path.GetDirectoryName(srcSourcePath);
                paths.AddRange(Directory.EnumerateFiles(sourceDirectory, "*.cob", SearchOption.AllDirectories));
            }

            if (paths.Count == 0)
                compiler.Messages.Add(Message.CannotFindImport, expression, expression.SourceFile);
            else foreach (var path in paths)
            {
                // HACK Caching the FileSystem level does not mean the compiler has already seen this file per se
                if (FileSystem.IsCached(path))
                    continue;

                var source = FileSystem.ReadAllText(path);
                var tokens = Tokenizer.Tokenize(source, path, compiler.Messages);
                var ast = Parser.Parse(tokens, compiler.Messages);

                // NOTE We perform a full forward declaration scan on any imported scripts meaning we incrementally
                //      pass through the declphase as we find imports
                var pass0 = new ForwardDeclaration(compiler);
                ast.Accept(pass0);

                compiler.Scripts.Add(ast);

                //if (initializerFunction != null)
                //{
                //    CurrentFunction.Body.Emit(Opcode.Call, initializerFunction.Operand, Array.Empty<Operand>());
                //    initializerFunction.Free();
                //}
            }

            return null;
        }

        private object VisitImportSymbol(ImportExpression expression)
        {
            if (Phase != DeclPhase.Functions)
                return null;

            // Symbol import
            Function? function;
            if (expression.SymbolTypeSignature != null)
            {
                function = new Function(
                    expression.SymbolName,
                    currentModule,
                    expression.SymbolTypeSignature.CallingConvention,
                    expression.SymbolTypeSignature.Parameters,
                    expression.SymbolTypeSignature.ReturnType
                );
            }
            else
                function = null;

            var import = new Import
            {
                Library = expression.SourceFile,
                SymbolName = expression.SymbolName,
                Function = function
            };

            compiler.Imports.Add(import);
                
            if (function != null)
            {
                function.NativeImport = import;
                var variable = new CobVariable(
                    expression.SymbolName,
                    new CobType(eCobType.Function, tag: function),
                    false
                );
                compiler.AllocateGlobal(variable);
                currentModule.Variables[variable.Name] = variable; // TODO Weird
            }

            return null;
        }

        public object Visit(ExportExpression expression)
        {
            return null; // NOTE Nothing to do
        }

        public object Visit(ArtifactExpression expression)
        {
            if (Phase != DeclPhase.Modules)
                return null;

            compiler.Artifacts.Add(expression);

            return null;
        }

        public object Visit(FunctionExpression expression)
        {
            if (Phase != DeclPhase.Functions)
                return null;

            CallingConvention callingConvention;
            IReadOnlyList<Function.Parameter> parameters;
            if (CurrentContext is TupleDefinitionExpression)
            {
                callingConvention = CallingConvention.ThisCall;
                var lParameters = new List<Function.Parameter>(expression.Parameters);
                lParameters.Insert(0, new Function.Parameter("this", new CobType(eCobType.Tuple, tag: CurrentContext), false));
                parameters = lParameters;
            }
            else
            {
                callingConvention = expression.CallingConvention;
                parameters = expression.Parameters;
            }

            var function = currentModule.AllocateFunction(
                expression.Name,
                callingConvention,
                parameters,
                expression.ReturnType
            );

            if (!expression.IsAnonymous)
            {
                var type = new CobType(eCobType.Function, tag: function);
                var variable = new CobVariable(expression.Name, type, false);
                compiler.AllocateGlobal(variable);
                currentModule.Variables[variable.Name] = variable; // TODO Weird
            }

            return null;
        }

        public object Visit(ReturnExpression expression)
        {
            return null;
        }

        public object Visit(AheadOfTimeExpression expression)
        {
            return null; // NOTE Nothing to do
        }

        public object Visit(FatArrowExpression expression)
        {
            expression.Expression.Accept(this);
            return null;
        }

        public object Visit(IfStatement expression)
        {
            return null;
        }

        public object Visit(ForStatement expression)
        {
            return null;
        }

        public object Visit(ContinueStatement expression)
        {
            return null;
        }

        public object Visit(BreakStatement expression)
        {
            return null;
        }

        public object Visit(BinaryOperatorExpression expression)
        {
            return null;
        }

        public object Visit(PrefixOperatorExpression expression)
        {
            return null;
        }

        public object Visit(PostfixOperatorExpression expression)
        {
            return null;
        }

        public object Visit(BlockExpression expression)
        {
            for (var i = 0; i < expression.Expressions.Count; ++i)
                expression.Expressions[i].Accept(this);

            return null;
        }

        public object Visit(CallExpression expression)
        {
            return null;
        }

        public object Visit(IdentifierExpression expression)
        {
            return null;
        }

        public object Visit(LensExpression expression)
        {
            return null;
        }

        public object Visit(IndexerExpression expression)
        {
            return null;
        }

        public object Visit(StructLiteralExpression expression)
        {
            return null;
        }

        public object Visit(NumberLiteralExpression expression)
        {
            return null;
        }

        public object Visit(BooleanLiteralExpression expression)
        {
            return null;
        }

        public object Visit(StringLiteralExpression expression)
        {
            return null;
        }

        public object Visit(EmptyExpression expression)
        {
            return null;
        }

        internal enum DeclPhase
        {
            Begin,
        
            ModuleImports = Begin,
            Modules,
            Types,
            Functions,
            Fields,

            Complete
        }
    }
}
