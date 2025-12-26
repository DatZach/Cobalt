using Compiler.Ast;
using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Ast.Visitors;
using Compiler.Lexer;
using Compiler.Utility;

namespace Compiler.CodeGeneration
{
    internal sealed class ForwardDeclaration : IExpressionVisitor<Unit>
    {
        public DeclPhase Phase { get; private set; }

        private Module CurrentModule => contextStack.OfType<Module>().First();

        private IScopeContext CurrentContext => contextStack.Peek();

        private readonly Stack<IScopeContext> contextStack;
        private readonly Compiler compiler;
        private readonly MessageCollection messages;

        public ForwardDeclaration(Compiler compiler, MessageCollection messages)
        {
            this.compiler = compiler;
            this.messages = messages;

            contextStack = new Stack<IScopeContext>(4);
        }

        public Unit Visit(ScriptExpression expression)
        {
            for (var phase = DeclPhase.Begin; phase < DeclPhase.Complete; ++phase)
            {
                contextStack.Clear();
                contextStack.Push(compiler.RootModule);

                Phase = phase;

                var expressions = expression.Expressions;
                for (int i = 0; i < expressions.Count; ++i)
                    expressions[i].Accept(this);
            }

            return Unit.Value;
        }

        public Unit Visit(ImportStatement expression)
        {
            // X import *
            // X import CobaltSourceFile
            // X import Directory.CobaltSourceFile
            // X import Directory.*
            // X import StandardLibraryCobaltFile
            
            //   import CobaltAssembly *
            //   import CobaltAssembly SpecificIdentifier

            // X import NativeLibrary SpecificIdentifier Type

            var isNativeImport = expression.SymbolName != null && expression.SymbolTypeSignature != null;
            if (!isNativeImport)
                return VisitImportModule(expression);
            else
                return VisitImportSymbol(expression);

            return Unit.Value;
        }

        private Unit VisitImportModule(ImportStatement expression)
        {
            if (Phase != DeclPhase.ModuleImports)
                return Unit.Value;

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
                messages.Add(Message.CannotFindImport, expression, expression.SourceFile);
            else foreach (var path in paths)
            {
                // HACK Caching the FileSystem level does not mean the compiler has already seen this file per se
                if (FileSystem.IsCached(path))
                    continue;

                var source = FileSystem.ReadAllText(path);
                var tokens = Tokenizer.Tokenize(source, path, messages);
                var ast = Parser.Parse(tokens, messages);

                // NOTE We perform a full forward declaration scan on any imported scripts meaning we incrementally
                //      pass through the declphase as we find imports
                var pass0 = new ForwardDeclaration(compiler, messages);
                ast.Accept(pass0);

                compiler.Scripts.Add(ast);
            }

            return Unit.Value;
        }

        private Unit VisitImportSymbol(ImportStatement expression)
        {
            if (Phase != DeclPhase.Functions)
                return Unit.Value;

            // Symbol import
            Function? function;
            if (expression.SymbolTypeSignature != null)
            {
                function = CurrentModule.AllocateFunction(
                    expression.SymbolName,
                    expression.SymbolTypeSignature.CallingConvention,
                    expression.SymbolTypeSignature.Parameters.Select(x => new Function.Parameter(x.Name, CobType.FromString(x.TypeName), x.IsSpread)).ToList(),
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
                function.NativeImport = import;

            return Unit.Value;
        }

        public Unit Visit(ExportStatement expression)
        {
            return Unit.Value; // NOTE Nothing to do
        }

        public Unit Visit(ArtifactStatement expression)
        {
            if (Phase != DeclPhase.Modules)
                return Unit.Value;

            compiler.Artifacts.Add(expression);

            return Unit.Value;
        }

        public Unit Visit(ModuleStatement expression)
        {
            Module module;
            if (Phase == DeclPhase.Modules)
                module = CurrentModule.FindOrAllocateModule(expression.Name);
            else if (Phase > DeclPhase.Modules)
                module = CurrentModule.FindModule(expression.Name)!;
            else
                return Unit.Value;

            contextStack.Push(module);

            if (expression.Block != null)
            {
                expression.Block.Accept(this);
                contextStack.Pop();
            }

            return Unit.Value;
        }

        public Unit Visit(TypeAliasStatement expression)
        {
            if (Phase != DeclPhase.Types)
                return Unit.Value;

            var typeName = CobType.FromString(expression.TypeName);
            if (!CobType.TryAddAlias(expression.Name, typeName))
                messages.Add(Message.SymbolConflictsWithOther, expression.Token, expression.Name);

            return Unit.Value;
        }

        public Unit Visit(TupleDeclStatement expression)
        {
            if (Phase == DeclPhase.Types)
            {
                var tupleType = CurrentModule.AllocateTupleType(expression.Name);
                var cobType = new CobType(eCobType.Tuple, tag: tupleType);

                if (!CobType.TryAddAlias(expression.Name, cobType))
                    messages.Add(Message.SymbolConflictsWithOther, expression.Token, expression.Name);
            }
            else if (Phase > DeclPhase.Types)
            {
                var tupleType = CurrentModule.FindTupleType(expression.Name)!;
                contextStack.Push(tupleType);
                
                for (var i = 0; i < expression.Functions.Count; ++i)
                    expression.Functions[i].Accept(this);

                if (Phase == DeclPhase.Fields)
                {
                    foreach (var field in expression.Fields)
                        tupleType.AllocateField(field.Name, CobType.FromString(field.TypeName), field.GetterExpression, field.SetterExpression);
                }

                contextStack.Pop();
            }
            
            return Unit.Value;
        }

        public Unit Visit(StructDeclStatement expression)
        {
            if (Phase == DeclPhase.Types)
            {
                var structType = CurrentModule.AllocateStructType(expression.Name);
                
                if (!CobType.TryAddAlias(expression.Name, new CobType(eCobType.Struct, tag: structType)))
                    messages.Add(Message.SymbolConflictsWithOther, expression.Token, expression.Name);
            }
            else if (Phase > DeclPhase.Types)
            {
                var structType = CurrentModule.FindStructType(expression.Name)!;
                contextStack.Push(structType);

                for (var i = 0; i < expression.Functions.Count; ++i)
                    expression.Functions[i].Accept(this);

                if (Phase == DeclPhase.Fields)
                {
                    foreach (var field in expression.Fields)
                        structType.AllocateField(field.Name, CobType.FromString(field.TypeName), field.GetterExpression, field.SetterExpression);

                    if (expression.Indexer != null)
                    {
                        structType.AllocateIndexer(
                            CobType.FromString(expression.Indexer.KeyTypeName),
                            CobType.FromString(expression.Indexer.ReturnTypeName),
                            expression.Indexer.GetterExpression,
                            expression.Indexer.SetterExpression
                        );
                    }
                }

                contextStack.Pop();
            }

            return Unit.Value;
        }

        public Unit Visit(FunctionDeclStatement expression)
        {
            if (Phase != DeclPhase.Functions)
                return Unit.Value;

            var parameters = expression.Parameters.Select(x => new Function.Parameter(x.Name, CobType.FromString(x.TypeName), x.IsSpread)).ToList();

            if (CurrentContext is TupleType tupleType)
            {
                if (expression.CallingConvention != CallingConvention.Default
                &&  expression.CallingConvention != CallingConvention.ThisCall)
                    messages.Add(Message.IllegalCallingConvention, expression, expression.CallingConvention);

                tupleType.AllocateFunction(expression.Name, parameters, expression.ReturnType);
            }
            else if (CurrentContext is StructType structType)
            {
                if (expression.CallingConvention != CallingConvention.Default
                &&  expression.CallingConvention != CallingConvention.ThisCall)
                    messages.Add(Message.IllegalCallingConvention, expression, expression.CallingConvention);

                structType.AllocateFunction(expression.Name, parameters, expression.ReturnType);
            }
            else if (CurrentContext is Module module)
            {
                module.AllocateFunction(expression.Name, expression.CallingConvention, parameters, expression.ReturnType);
            }
            else
                messages.Add(Message.CannotDeclareSymbolHere, expression);

            return Unit.Value;
        }

        public Unit Visit(VariableDeclStatement expression)
        {
            // NOTE We do not dive function bodies in this visitor so any VarExpressions we visit will be
            //      module-level declarations

            if (Phase != DeclPhase.Fields)
                return Unit.Value;

            var mutable = expression.Type == TokenType.Var;
            foreach (var decl in expression.Declarations)
            {
                CurrentModule.AllocateGlobal(decl.Name, decl.Type, mutable);
            }

            return Unit.Value;
        }

        public Unit Visit(IfStatement expression)
        {
            return Unit.Value;
        }

        public Unit Visit(ForStatement expression)
        {
            return Unit.Value;
        }

        public Unit Visit(ContinueStatement expression)
        {
            return Unit.Value;
        }

        public Unit Visit(BreakStatement expression)
        {
            return Unit.Value;
        }

        public Unit Visit(ReturnStatement expression)
        {
            return Unit.Value;
        }

        public Unit Visit(BlockExpression expression)
        {
            for (var i = 0; i < expression.Expressions.Count; ++i)
                expression.Expressions[i].Accept(this);

            return Unit.Value;
        }

        public Unit Visit(FatArrowStatement expression)
        {
            expression.Expression.Accept(this);
            return Unit.Value;
        }

        public Unit Visit(BinaryOperatorExpression expression)
        {
            return Unit.Value;
        }

        public Unit Visit(PrefixOperatorExpression expression)
        {
            return Unit.Value;
        }

        public Unit Visit(PostfixOperatorExpression expression)
        {
            return Unit.Value;
        }

        public Unit Visit(CallExpression expression)
        {
            return Unit.Value;
        }

        public Unit Visit(IdentifierExpression expression)
        {
            return Unit.Value;
        }

        public Unit Visit(AheadOfTimeExpression expression)
        {
            return Unit.Value;
        }

        public Unit Visit(LensExpression expression)
        {
            return Unit.Value;
        }

        public Unit Visit(IndexerExpression expression)
        {
            return Unit.Value;
        }

        public Unit Visit(StructLiteralExpression expression)
        {
            return Unit.Value;
        }

        public Unit Visit(NumberLiteralExpression expression)
        {
            return Unit.Value;
        }

        public Unit Visit(BooleanLiteralExpression expression)
        {
            return Unit.Value;
        }

        public Unit Visit(StringLiteralExpression expression)
        {
            return Unit.Value;
        }

        public Unit Visit(EmptyExpression expression)
        {
            return Unit.Value;
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
