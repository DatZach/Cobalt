using Compiler.Ast;
using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Ast.Visitors;
using Compiler.CodeGeneration.Artifacts;
using Compiler.Lexer;
using Compiler.Utility;
using CallingConvention = Compiler.CodeGeneration.Artifacts.CallingConvention;

namespace Compiler.CodeGeneration
{
    internal sealed class ForwardDeclaration : IExpressionVisitor<Unit>
    {
        private Module CurrentModule => contextStack.OfType<Module>().First();

        private IScopeContext CurrentContext => contextStack.Peek();

        private readonly Stack<IScopeContext> contextStack;
        private readonly DeclPhase phase;
        private readonly Compiler compiler;
        private readonly MessageCollection messages;

        public ForwardDeclaration(DeclPhase phase, Compiler compiler, MessageCollection messages)
        {
            this.phase = phase;
            this.compiler = compiler;
            this.messages = messages;

            contextStack = new Stack<IScopeContext>(4);
        }

        public Unit Visit(ScriptExpression expression)
        {
            contextStack.Clear();
            contextStack.Push(compiler.RootModule);

            var expressions = expression.Expressions;
            for (int i = 0; i < expressions.Count; ++i)
                expressions[i].Accept(this);

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

            var isNativeImport = expression.SymbolName != null && expression.SymbolTypeName != null;
            if (!isNativeImport)
                return VisitImportModule(expression);
            else
                return VisitImportSymbol(expression);

            return Unit.Value;
        }

        private Unit VisitImportModule(ImportStatement expression)
        {
            if (phase != DeclPhase.ModuleImports)
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
                var pass0 = new ForwardDeclaration(phase, compiler, messages);
                ast.Accept(pass0);

                compiler.Scripts.Add(ast);
            }

            return Unit.Value;
        }

        private Unit VisitImportSymbol(ImportStatement expression)
        {
            if (phase != DeclPhase.Functions)
                return Unit.Value;

            // Symbol import
            Function? function;
            if (expression.SymbolTypeName != null)
            {
                function = CurrentModule.AllocateFunction(
                    expression.SymbolName,
                    expression.SymbolTypeName.Function.CallingConvention,
                    expression.SymbolTypeName.Function.Parameters.Select(x => new Function.Parameter(x.Name, ResolveTypeFromTypeName(x.TypeName, expression), x.IsSpread)).ToList(),
                    ResolveTypeFromTypeName(expression.SymbolTypeName.Function.ReturnTypeName, expression)
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
            if (phase != DeclPhase.Modules)
                return Unit.Value;

            var containerParameters = new Dictionary<string, Variable>();
            if (expression.ContainerParameters != null)
            {
                foreach (var containerParameter in expression.ContainerParameters)
                {
                    if (containerParameter.Operator != TokenType.Assign)
                    {
                        messages.Add(Message.UnexpectedToken2, expression, TokenType.Assign, containerParameter.Operator);
                        continue;
                    }

                    if (containerParameter.Left is not IdentifierExpression lhs)
                    {
                        messages.Add(Message.UnexpectedToken2, expression, TokenType.Identifier, containerParameter.Left);
                        continue;
                    }

                    var rhs = new Variable(lhs.Value, CobType.None);
                    containerParameters.Add(lhs.Value, rhs);
                }
            }

            compiler.ArtifactDirectives.Add(new ArtifactDirective
            {
                Container = expression.Container,
                Architecture = expression.Architecture,
                Filename = expression.Filename,
                ContainerConfiguration = containerParameters
            });

            return Unit.Value;
        }

        public Unit Visit(ModuleStatement expression)
        {
            Module module;
            if (phase == DeclPhase.Modules)
                module = CurrentModule.FindOrAllocateModule(expression.Name);
            else if (phase > DeclPhase.Modules)
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

        public Unit Visit(TypeDeclStatement expression)
        {
            if (phase == DeclPhase.Types)
            {
                if (CurrentModule.FindDistinctType(expression.SubTypeName) != null)
                {
                    messages.Add(Message.SymbolConflictsWithOther, expression.Token, expression.SubTypeName);
                    return Unit.Value;
                }

                // type Sub = Super;
                var superType = ResolveTypeFromTypeName(expression.SuperTypeName, expression);

                // TODO Not strictly correct to put the tag of the super type on the sub type
                var tag = superType.Tag;
                if (expression.RefinementExpression is EnumRefinementDeclStatement)
                    tag = new EnumRefinement(expression.SubTypeName, CurrentContext, compiler);
                
                var subType = CurrentModule.AllocateDistinctType(
                    expression.SubTypeName,
                    superType,
                    tag,
                    expression.RefinementExpression
                );

                if (tag is EnumRefinement enumRefinement
                &&  expression.RefinementExpression is EnumRefinementDeclStatement)
                {
                    ((ILateTypeBinding)enumRefinement).RebindType(subType);

                    contextStack.Push(enumRefinement);
                    expression.RefinementExpression.Accept(this);
                    contextStack.Pop();
                }

                //contextStack.Push(subType);
                if (tag is RecordType recordType
                &&  expression.SuperTypeName.Record != null)
                {
                    HACK_ContextAlreadyDived = true;
                    contextStack.Push(recordType);
                    expression.SuperTypeName.Record.Accept(this);
                    contextStack.Pop();
                    HACK_ContextAlreadyDived = false;
                }

                // TODO Can this be moved back into Intrinsics.Pass1?
                if (expression.SubTypeName == "Array")
                    Intrinsics.Array = subType;
                else if (expression.SubTypeName == "POD")
                    Intrinsics.POD = subType;
            }
            else if (phase > DeclPhase.Types)
            {
                //expression.SuperTypeName.Record2?.Accept(this);
                var superType = CurrentModule.FindSymbol(expression.SubTypeName);
                if (superType?.Type.Parent?.Tag is IScopeContext ctx)
                {
                    HACK_ContextAlreadyDived = true;
                    contextStack.Push(ctx);
                    expression.SuperTypeName.Record?.Accept(this);
                    contextStack.Pop();
                    HACK_ContextAlreadyDived = false;
                }
            }

            return Unit.Value;
        }

        public Unit Visit(EnumRefinementDeclStatement expression)
        {
            if (phase != DeclPhase.Types)
                return Unit.Value;

            var enumRefinement = (EnumRefinement)CurrentContext;
            var subType = enumRefinement.ThisType;
            var superType = enumRefinement.ThisType.Parent!;

            var stringType = compiler.RootModule.FindDistinctType("string"); // HACK 1st Pass Intrinsics not yet evaluated
            if (!CobType.IsImplicitCastable(superType, CobType.Int)
            &&  !CobType.IsImplicitCastable(superType, CobType.UInt)
            &&  !CobType.IsImplicitCastable(superType, stringType))
            {
                messages.Add(Message.IllegalEnumRefinementSuperType, expression, superType);
                return Unit.Value;
            }

            long value = 0;
            int mode = 0; // 0 integer, 1 bitflag
            foreach (var member in expression.Members)
            {
                if (member.Name != "_" && enumRefinement.FindMember(member.Name) != null)
                {
                    messages.Add(Message.SymbolConflictsWithOther, member.Token, member.Name);
                    continue;
                }

                string memberName = member.Name;
                object memberValue;
                if (superType == stringType)
                {
                    if (member.Value is StringLiteralExpression sle)
                        memberValue = sle.Value;
                    else if (member.Value is null)
                        memberValue = memberName;
                    else
                    {
                        messages.Add(Message.TypeMismatch, member.Value, superType, member.Value.Token);
                        memberValue = "";
                    }
                }
                else
                {
                    if (member.Value is NumberLiteralExpression nle)
                    {
                        value = nle.LongValue;
                        mode = nle.Token.Value.StartsWith("0b") || nle.Token.Value.StartsWith("0x") ? 1 : 0;
                    }
                    else if (member.Value is PrefixOperatorExpression poe && poe.Operator == TokenType.Dot
                         &&  poe.Right is IdentifierExpression poeIe)
                    {
                        var eMember = enumRefinement.FindMember(poeIe.Value);
                        if (eMember == null)
                            messages.Add(Message.UndeclaredIdentifier, member.Token, poeIe.Value);
                        else
                            value = eMember.Value.Value;
                    }
                    else if (member.Value is not null)
                        messages.Add(Message.TypeMismatch, member.Value, superType, member.Value.Token);

                    if (!CobType.IsIntegerTypeCapableOfStoringValueWithoutTruncation(superType, CobType.Int, value))
                        messages.Add(Message.ValueOverflow, member.Token);

                    memberValue = value;

                    if (mode == 0)
                        ++value;
                    else if (mode == 1)
                        value = value == 0 ? 1 : value << 1;
                    else
                        throw new InvalidOperationException();
                }

                if (memberName != "_")
                    enumRefinement.AllocateMember(memberName, memberValue);
            }

            return Unit.Value;
        }

        public Unit Visit(ErrorStatement expression)
        {
            if (phase == DeclPhase.Types)
            {
                foreach (var x in expression.Members)
                {
                    var error = compiler.Errors.AllocateGlobal(x, CobType.Error, false);
                    error.IntValue = compiler.Globals.Count;
                }
            }

            return Unit.Value;
        }

        private bool HACK_ContextAlreadyDived;
        public Unit Visit(RecordStatement expression)
        {
            if (phase == DeclPhase.Types)
            {
                RecordType recordType;
                if (!HACK_ContextAlreadyDived)
                {
                    if (CurrentModule.FindRecordType(expression.Name) != null)
                    {
                        messages.Add(Message.SymbolConflictsWithOther, expression.Token, expression.Name);
                        return Unit.Value;
                    }

                    recordType = CurrentModule.AllocateRecordType(expression.Name, eRecordType.Struct);
                    contextStack.Push(recordType);
                }
                else
                    recordType = (RecordType)CurrentContext;

                foreach (var generic in expression.Generics)
                    recordType.AttachGeneric(generic);

                foreach (var traitTypeName in expression.Mixins)
                {
                    var traitType = ResolveTypeFromTypeName(traitTypeName, expression);
                    if (traitType.TagRecord == null || !traitType.TagRecord.IsTrait)
                    {
                        messages.Add(Message.CannotMixinNonTraitType, expression, traitTypeName);
                        continue;
                    }

                    recordType.AttachTrait(traitType.TagRecord);
                }

                for (var i = 0; i < expression.Types.Count; ++i)
                    expression.Types[i].Accept(this);

                if (!HACK_ContextAlreadyDived)
                    contextStack.Pop();
            }
            else if (phase > DeclPhase.Types)
            {
                var recordType = CurrentModule.FindRecordType(expression.Name)!;
                contextStack.Push(recordType);

                for (var i = 0; i < expression.Constants.Count; ++i)
                    expression.Constants[i].Accept(this);

                for (var i = 0; i < expression.Constructors.Count; ++i)
                    expression.Constructors[i].Accept(this);

                for (var i = 0; i < expression.Destructors.Count; ++i)
                    expression.Destructors[i].Accept(this);

                for (var i = 0; i < expression.Functions.Count; ++i)
                    expression.Functions[i].Accept(this);

                if (phase == DeclPhase.Fields)
                {
                    for (var i = 0; i < expression.Fields.Count; ++i)
                    {
                        var field = expression.Fields[i];
                        var fieldName = field.Name;
                        var fieldType = ResolveTypeFromTypeName(field.TypeName, expression);

                        if (fieldName == null || fieldName == "_")
                        {
                            if (i > 0 && !recordType.IsBare && recordType.Type == eRecordType.Tuple)
                                messages.Add(Message.IllegalFieldForTuple, expression);

                            fieldName = $"'{i}";
                        }
                        else if (i > 0 && recordType.IsBare && recordType.Type == eRecordType.Tuple)
                            messages.Add(Message.IllegalFieldForBareTuple, expression);

                        recordType.AllocateField(fieldName, fieldType, field.IsFinal, field.GetterExpression != null, field.SetterExpression != null);
                    }

                    if (expression.Indexer != null)
                    {
                        var keyType = ResolveTypeFromTypeName(expression.Indexer.KeyTypeName, expression);
                        var returnType = ResolveTypeFromTypeName(expression.Indexer.ReturnTypeName, expression);

                        recordType.AllocateIndexer(keyType, returnType);
                    }
                }

                if (phase == DeclPhase.Fields && recordType.HasTrait(Intrinsics.POD.TagRecord))
                {
                    if (expression.Mixins.Count > 1)
                        messages.Add(Message.PODIllegalMixin, expression);

                    long size = 0;
                    for (var i = 0; i < expression.Fields.Count; ++i)
                    {
                        var field = expression.Fields[i];
                        if (field.Name == null || field.Name == "SIZE")
                        {
                            messages.Add(Message.PODIllegalField, expression, field.Name);
                            continue;
                        }

                        if (field.GetterExpression != null)
                            continue;

                        var fieldName = field.Name == null || field.Name == "_" ? $"'{i}" : field.Name;
                        var sField = recordType.FindField(fieldName);
                        if (sField == null || sField.Type.Size <= 0)
                            messages.Add(Message.PODIllegalField, expression, field.Name);
                        else
                            size += sField.Type.Size / 8;
                    }

                    recordType.AllocateGlobal("SIZE", CobType.UInt, false).Value = size;
                }

                contextStack.Pop();
            }

            return Unit.Value;
        }

        public Unit Visit(ConstructStatement expression)
        {
            if (phase != DeclPhase.Functions)
                return Unit.Value;

            var parameters = expression.Parameters.Select(
                x => new Function.Parameter(x.Name, ResolveTypeFromTypeName(x.TypeName, expression), x.IsSpread, x.DefaultValue)
            ).ToList();

            if (CurrentContext is RecordType recordType)
            {
                recordType.AllocateConstructor(expression.Name, parameters, expression.Body == null);
            }
            else
                messages.Add(Message.CannotDeclareSymbolHere, expression);

            return Unit.Value;
        }

        public Unit Visit(DestructStatement expression)
        {
            if (phase != DeclPhase.Functions)
                return Unit.Value;

            var parameters = expression.Parameters.Select(
                x => new Function.Parameter(x.Name, ResolveTypeFromTypeName(x.TypeName, expression), x.IsSpread, x.DefaultValue)
            ).ToList();

            if (CurrentContext is RecordType recordType)
            {
                var returnType = ResolveTypeFromTypeName(expression.ReturnTypeName, expression);
                recordType.AllocateDestructor(expression.Name, parameters, returnType, expression.Body == null);
            }
            else
                messages.Add(Message.CannotDeclareSymbolHere, expression);

            return Unit.Value;
        }

        public Unit Visit(FunctionDeclStatement expression)
        {
            if (phase == DeclPhase.Types)
            {

            }
            else if (phase == DeclPhase.Functions)
            {
                if (expression.Body == null
                &&  (CurrentContext is not RecordType tRecordType || !tRecordType.IsTrait))
                {
                    messages.Add(Message.MissingFunctionBody, expression);
                    return Unit.Value;
                }

                var parameters = expression.Parameters.Select(
                    x => new Function.Parameter(x.Name, ResolveTypeFromTypeName(x.TypeName, expression), x.IsSpread, x.DefaultValue)
                ).ToList();

                // HACK Return Type cannot result This because the function context has not yet been allocated
                //      Need to find a way to improve this, maybe late binding return types?
                var returnTypeName = expression.ReturnTypeName;
                if (returnTypeName != null
                &&  returnTypeName.Type == eTypeName.Identifier && returnTypeName.Identifier == "This")
                    returnTypeName = new TypeName { Type = eTypeName.Identifier, Identifier = expression.Name };

                var returnType = ResolveTypeFromTypeName(returnTypeName, expression);

                
                if (CurrentContext is RecordType recordType)
                {
                    if (expression.CallingConvention != CallingConvention.Default
                    &&  expression.CallingConvention != CallingConvention.ThisCall)
                        messages.Add(Message.IllegalCallingConvention, expression, expression.CallingConvention);

                    recordType.AllocateFunction(expression.Name, parameters, returnType, expression.Body == null);
                }
                else if (CurrentContext is Module module)
                {
                    module.AllocateFunction(expression.Name, expression.CallingConvention, parameters, returnType);
                }
                else
                    messages.Add(Message.CannotDeclareSymbolHere, expression);
            }
            else if (phase >= DeclPhase.Fields)
            {
                foreach (var parameter in expression.Parameters)
                {
                    var type = ResolveTypeFromTypeName(parameter.TypeName, expression);
                    if (type.Type == eCobType.Record && type.Tag is IScopeContext ctx
                    &&  parameter.TypeName.Type == eTypeName.RecordSignature)
                    {
                        HACK_ContextAlreadyDived = true;
                        contextStack.Push(ctx);
                        parameter.TypeName.Record?.Accept(this);
                        contextStack.Pop();
                        HACK_ContextAlreadyDived = false;
                    }
                }
            }

            return Unit.Value;
        }

        public Unit Visit(MixinDeclStatement expression)
        {
            if (phase != DeclPhase.Functions)
                return Unit.Value;

            var context = CurrentModule.FindSymbol(expression.TargetTypeName) as IScopeContext;
            if (context != null)
            {
                contextStack.Push(context);
                expression.Function?.Accept(this);

                if (expression.Field != null)
                {
                    var fieldType = ResolveTypeFromTypeName(expression.Field.TypeName, expression);
                    if (context is RecordType recordType)
                        recordType.AllocateField(expression.Field.Name, fieldType, expression.Field.IsFinal, expression.Field.GetterExpression != null, expression.Field.SetterExpression != null);
                    else
                        messages.Add(Message.CannotDeclareSymbolHere, expression);
                }
                
                contextStack.Pop();
            }
            else
                messages.Add(Message.UndeclaredIdentifier, expression, expression.TargetTypeName);

            return Unit.Value;
        }

        public Unit Visit(VariableDeclStatement expression)
        {
            // NOTE We do not dive function bodies in this visitor so any VarExpressions we visit will be
            //      module-level declarations

            if (phase != DeclPhase.Fields)
                return Unit.Value;

            var mutable = expression.Type == TokenType.Var;
            foreach (var decl in expression.Declarations)
            {
                if (decl is VariableDeclStatement.StandardDeclaration stdDecl)
                {
                    if (CurrentContext is Module module)
                        module.AllocateGlobal(stdDecl.Name, CobType.None, mutable);
                    else if (CurrentContext is RecordType recordType)
                        recordType.AllocateGlobal(stdDecl.Name, CobType.None, mutable);
                    else
                        messages.Add(Message.CannotDeclareSymbolHere, expression); // idk maybe support some day
                }
                else
                    messages.Add(Message.CannotDeclareSymbolHere, expression); // idk maybe support some day
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

        public Unit Visit(WhileStatement expression)
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

        public Unit Visit(DeferStatement expression)
        {
            return Unit.Value;
        }

        public Unit Visit(ReturnStatement expression)
        {
            return Unit.Value;
        }

        public Unit Visit(MachineStatement expression)
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

        public Unit Visit(PatternMatchExpression expression)
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

        public Unit Visit(TupleLiteralExpression expression)
        {
            return Unit.Value;
        }

        public Unit Visit(StructLiteralExpression expression)
        {
            return Unit.Value;
        }

        public Unit Visit(ArrayLiteralExpression expression)
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

        public Unit Visit(CharacterLiteralExpression expression)
        {
            return Unit.Value;
        }

        public Unit Visit(NilLiteralExpression expression)
        {
            return Unit.Value;
        }

        public Unit Visit(EmptyExpression expression)
        {
            return Unit.Value;
        }

        private CobType ResolveTypeFromTypeName(TypeName? typeName, Expression expression)
        {
            var result = CobType.FromTypeName(typeName, CurrentContext);
            if (result == null)
            {
                messages.Add(Message.IllegalTypeName, expression, typeName);
                return CobType.None;
            }

            return result;
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
