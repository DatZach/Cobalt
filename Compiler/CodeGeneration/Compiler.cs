using Compiler.Ast;
using Compiler.Ast.Expressions;
using Compiler.Ast.Expressions.Statements;
using Compiler.Ast.Visitors;
using Compiler.Interpreter;
using Compiler.Lexer;
using System.Diagnostics;
using System.Text;

namespace Compiler.CodeGeneration
{
    internal sealed class Compiler : IExpressionVisitor<Storage?>
    {
        public List<ArtifactExpression> Artifacts { get; }

        public List<Import> Imports { get; }

        public List<Export> Exports { get; }

        public List<Module> Modules { get; }

        public List<CobVariable> Globals { get; } // TODO Remove

        public Function? EntryFunction { get; private set; }

        // TODO Possible to collapse contextStack and functionStack into 1?
        public Function? CurrentFunction => functionStack.Count == 0 ? null : functionStack.Peek();

        public IContext ParentContext => contextStack.Skip(1).FirstOrDefault();

        private IContext CurrentContext => contextStack.Peek();

        public Module CurrentModule { get; private set; } // TODO Make this private again

        public MessageCollection Messages { get; } // TODO Make this private again, somehow

        private readonly Module rootModule;
        private readonly Stack<Function> functionStack;
        private readonly Stack<IContext> contextStack; // TODO IScopedContext or something (Struct, Module, Tuple, etc.)
        
        private Compiler(MessageCollection messages)
        {
            Artifacts = new List<ArtifactExpression>();
            Imports = new List<Import>();
            Exports = new List<Export>();
            Modules = new List<Module>();
            Globals = new List<CobVariable>();
            functionStack = new Stack<Function>();
            contextStack = new Stack<IContext>();

            CurrentModule = rootModule = new Module(this, null);
            AllocateGlobal(new CobVariable(CurrentModule.InitializerFunction.FullyQualifiedName, new CobType(eCobType.Function, 0, tag: CurrentModule.InitializerFunction), false)); // HACK Awful. Global should be scoped to a Module
            Modules.Add(rootModule);

            this.Messages = messages ?? throw new ArgumentNullException(nameof(messages));
        }

        public Storage? Visit(ScriptExpression expression)
        {
            contextStack.Push(rootModule);
            functionStack.Push(CurrentModule.InitializerFunction);

            // HACK For now, let's just call a special function that invokes all our module initializers in the
            //      program's entry point
            //Function? hackInitializers;
            //if (contextStack.Count == 1)
            //{
            //    hackInitializers = CurrentModule.AllocateFunction(
            //        "$HACK_InvokeInitializers",
            //        CallingConvention.CCall,
            //        Array.Empty<Function.Parameter>(),
            //        CobType.None
            //    );
            //    var varGlobHackInitializers = new CobVariable(hackInitializers.FullyQualifiedName,
            //        new CobType(eCobType.Function, 0, tag: hackInitializers), false);
            //    var globHackInitializers =
            //        AllocateGlobal(varGlobHackInitializers); // HACK Awful. Global should be scoped to a Module

            //    CurrentFunction.Body.Emit(
            //        Opcode.Call,
            //        new Operand { Type = OperandType.Global, Value = globHackInitializers },
            //        Array.Empty<Operand>()
            //    );
            //}
            //else
            //    hackInitializers = null;

            var expressions = expression.Expressions;
            for (int i = 0; i < expressions.Count; ++i)
                expressions[i].Accept(this);

            CurrentModule.InitializerFunction.Body.Emit(Opcode.Return);

            // TODO Needs to be unified with the copy-pasted code in ModuleExpression
            contextStack.Pop();
            CurrentModule = contextStack.Count == 0 ? rootModule : contextStack.Peek() as Module ?? rootModule;
            
            functionStack.Pop();

            // HACK Populate the initializers calling function from earlier now that we know what all our modules
            //      are
            //if (hackInitializers != null)
            //{
            //    foreach (var module in Modules)
            //    {
            //        hackInitializers.Body.Emit(
            //            Opcode.Call,
            //            new Operand
            //            {
            //                Type = OperandType.Global,
            //                Value = FindGlobal(module.InitializerFunction.FullyQualifiedName),
            //                Size = 0
            //            },
            //            Array.Empty<Operand>()
            //        );
            //    }

            //    hackInitializers.Body.Emit(Opcode.Return);
            //}

            return null;
        }

        public Storage? Visit(ModuleExpression expression)
        {
            var module = Modules.FirstOrDefault(x => x.Name == expression.Name);
            if (module == null)
            {
                module = new Module(this, expression.Name);
                AllocateGlobal(new CobVariable(
                    module.InitializerFunction.FullyQualifiedName,
                    new CobType(eCobType.Function, 0, tag: module.InitializerFunction),
                    false)); // HACK Awful. Global should be scoped to a Module
                Modules.Add(module);
            }

            // TODO Remove this rule and implement AllocateModule
            if (CurrentModule != rootModule && module != rootModule)
            {
                Messages.Add(Message.CannotNestModules, expression);
                return null;
            }

            var prevModule = CurrentModule;
            CurrentModule = module;
            contextStack.Push(module);

            functionStack.Push(module.InitializerFunction);

            if (expression.Block != null)
            {
                var storage = expression.Block.Accept(this);
                storage?.Free();

                // TODO Needs to be unified with the copy-pasted code in ScriptExpression
                contextStack.Pop();
                CurrentModule = prevModule;

                functionStack.Pop();

                module.InitializerFunction.Body.Emit(Opcode.Return);
            }

            return null;
        }

        public Storage? Visit(TypeExpression expression)
        {
            // NOTE Type Alias added by the Lexer/Parselet
            return null;
        }

        public Storage? Visit(TupleDefinitionExpression expression)
        {
            CurrentModule.TupleTypes.Add(expression);

            contextStack.Push(expression);
            foreach (var functionExpression in expression.Functions)
                functionExpression.Accept(this);
            contextStack.Pop();

            return null;
        }

        public Storage? Visit(StructDefinitionExpression expression)
        {
            CurrentModule.StructTypes.Add(expression);

            contextStack.Push(expression);
            foreach (var functionExpression in expression.Functions)
                functionExpression.Accept(this);
            contextStack.Pop();

            return null;
        }

        public Storage? Visit(VarExpression expression)
        {
            for (var i = 0; i < expression.Declarations.Count; ++i)
            {
                var decl = expression.Declarations[i];
                var mutable = expression.Type == TokenType.Var;
                var type = decl.Type;

                Storage? rhs;
                if (decl.Initializer != null)
                {
                    rhs = decl.Initializer.Accept(this);
                    if (rhs == null)
                        Messages.Add(Message.TypeMismatch, expression, "any", "none");
                    else if (type != null && !CobType.IsCastable(rhs.Type, type))
                        Messages.Add(Message.TypeMismatch, expression, type, rhs.Type);
                    else
                        type = rhs.Type;
                }
                else
                    rhs = null;

                if ((decl.Type == null && decl.Initializer == null)
                ||  type == null)
                {
                    Messages.Add(Message.MalformedVarDeclaration, expression);
                    continue;
                }
                
                var variable = new CobVariable(decl.Name, type, mutable);

                if (CurrentFunction != null)
                {
                    if (CurrentFunction == CurrentModule.InitializerFunction) // Global Decl
                    {
                        var global = AllocateGlobal(variable);
                        if (rhs != null)
                        {
                            CurrentFunction.Body.Emit( // TODO Not right
                                Opcode.Move,
                                new Operand { Type = OperandType.Global, Value = global, Size = type.Size },
                                rhs.Operand
                            ); // TODO EmitLO
                        }
                    }
                    else
                    {
                        var local = CurrentFunction.AllocateLocal(variable);
                        if (rhs != null)
                        {
                            CurrentFunction.Body.Emit(
                                Opcode.Move,
                                new Operand { Type = OperandType.Local, Value = local, Size = type.Size },
                                rhs.Operand
                            ); // TODO EmitLO
                        }
                    }
                }
                else
                    throw new NotImplementedException(); // TODO Normal compile error? What does this mean??

                rhs?.Free();
            }

            return null;
        }

        public Storage? Visit(ImportExpression expression)
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
            {
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
                    Messages.Add(Message.CannotFindImport, expression, expression.SourceFile);
                else foreach (var path in paths)
                {
                    // HACK Caching the FileSystem level does not mean the compiler has already seen this file per se
                    if (FileSystem.IsCached(path))
                        continue;

                    var source = FileSystem.ReadAllText(path);
                    var tokens = Tokenizer.Tokenize(source, path, Messages);
                    var ast = Parser.Parse(tokens, Messages);
                    ast.Accept(this);

                    //if (initializerFunction != null)
                    //{
                    //    CurrentFunction.Body.Emit(Opcode.Call, initializerFunction.Operand, Array.Empty<Operand>());
                    //    initializerFunction.Free();
                    //}
                }
            }
            else
            {
                // Symbol import
                Function? function;
                if (expression.SymbolTypeSignature != null)
                {
                    function = new Function(
                        expression.SymbolName,
                        CurrentModule,
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

                Imports.Add(import);
                
                if (function != null)
                {
                    function.NativeImport = import;
                    AllocateGlobal(new CobVariable(
                        expression.SymbolName,
                        new CobType(eCobType.Function, tag: function),
                        false
                    ));
                }
            }

            return null;
        }

        public Storage? Visit(ExportExpression expression)
        {
            var function = expression.FunctionExpression.Accept(this);
            var export = new Export { Function = function.Type.TagFunction };
            Exports.Add(export);
            return null;
        }

        public Storage? Visit(ArtifactExpression expression)
        {
            Artifacts.Add(expression);
            return null;
        }

        public Storage? Visit(IfStatement expression)
        {
            ++conditionalStack;
            var conditional = expression.Conditional.Accept(this);
            --conditionalStack;

            if (conditional == null || conditional.Type != CobType.Boolean)
            {
                Messages.Add(Message.TypeMismatch, expression.Conditional, CobType.Boolean, conditional?.Type.ToString() ?? "none");
                return null;
            }
            else if (expression.Conditional is IdentifierExpression)
            {
                var c = CurrentFunction.AllocateStorage(CobType.Boolean);
                CurrentFunction.Body.Emit(Opcode.Compare, c.Operand, conditional.Operand, new Operand { Type = OperandType.ImmediateUnsigned, Value = 1 });
                conditional.Free();
                conditional = c;
            }

            var elseLabel = CurrentFunction.Body.AllocateLabel();
            var endLabel = CurrentFunction.Body.AllocateLabel();
            CurrentFunction.Body.Emit(Opcode.JumpIfF, conditional.Operand, elseLabel);
            conditional.Free();

            expression.Then.Accept(this)?.Free();
            CurrentFunction.Body.Emit(Opcode.Jump, endLabel);

            elseLabel.Mark();

            expression.Else?.Accept(this)?.Free();

            endLabel.Mark();

            return null;
        }

        public Storage? Visit(ReturnExpression expression)
        {
            if (expression.Expression == null)
                CurrentFunction.Body.Emit(Opcode.Return);
            else
            {
                var rhs = expression.Expression.Accept(this);
                if (CurrentFunction.ReturnType == eCobType.None)
                    CurrentFunction.ReturnType = rhs.Type;
                else if (CurrentFunction.ReturnType != rhs.Type)
                    Messages.Add(Message.ReturnTypeMismatch, expression);
                
                CurrentFunction.Body.Emit(Opcode.Return, rhs.Operand);
                rhs.Free();
            }

            return null;
        }

        public Storage? Visit(AheadOfTimeExpression expression)
        {
            var function = new Function(
                "$aot_eval$",
                CurrentModule,
                CallingConvention.CCall,
                Array.Empty<Function.Parameter>(),
                CobType.None
            );

            functionStack.Push(function);
            var evalStorage = expression.Expression.Accept(this);
            if (evalStorage == null || evalStorage.Type == eCobType.None)
                Messages.Add(Message.AotCannotUseVoid, expression);

            function.ReturnType = evalStorage.Type;
            CurrentFunction.Body.Emit(Opcode.Return, evalStorage.Operand);
            evalStorage.Free();

            function.ReturnLabel.Mark();
            
            functionStack.Pop();

            using var vm = new VirtualMachine(this);
            var result = vm.ExecuteFunction(function);

            var reg = CurrentFunction.AllocateStorage(function.ReturnType);
            // TODO EmitOI
            CurrentFunction.Body.Emit(
                Opcode.Move,
                reg.Operand,
                new Operand { Type = OperandType.ImmediateUnsigned, Size = -1, Value = result.IntValue } // TODO Not right
            );
            
            return evalStorage;
        }

        public Storage? Visit(FatArrowExpression expression)
        {
            var value = expression.Expression.Accept(this);
            if (CurrentModule == rootModule && value != null && value.Type == eCobType.Function)
            {
                if (EntryFunction != null)
                    Messages.Add(Message.CannotRedeclareEntryPoint, expression);
                else
                {
                    EntryFunction = value.Type.TagFunction;
                }
            }

            return value;
        }

        public Storage? Visit(FunctionExpression expression)
        {
            if (expression.Body == null)
            {
                Messages.Add(Message.MissingFunctionBody, expression);
                return null;
            }

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

            var function = CurrentModule.AllocateFunction(
               expression.Name,
                callingConvention,
                parameters,
                expression.ReturnType
            );

            var type = new CobType(eCobType.Function, tag: function);
            if (!expression.IsAnonymous)
                AllocateGlobal(new CobVariable(expression.Name, type, false));
            
            functionStack.Push(function);
            contextStack.Push(function);
            
            expression.Body.Accept(this);
            
            function.ReturnLabel.Mark();
            function.Body.Emit(Opcode.Return); // TODO Error if not all paths return

            contextStack.Pop();
            functionStack.Pop();

            function.Body.HACK_Optmize();

            return new Storage(
                CurrentFunction,
                new Operand(), //null, // TODO ???
                type
            );
        }

        public Storage? BinOpLHS { get; set; } // TODO Probably a hack tbh
        public Storage? AssignmentRHS { get; set; }
        private int conditionalStack;

        public Storage? Visit(BinaryOperatorExpression expression)
        {
            // Assignment
            var asnOpcode = expression.Operator switch
            {
                TokenType.AddAssign => Opcode.Add,
                TokenType.SubtractAssign => Opcode.Sub,
                TokenType.MultiplyAssign => Opcode.Mul,
                TokenType.DivideAssign => Opcode.Div,
                TokenType.DivideCeilAssign => Opcode.DivCeil,
                TokenType.DivideFloorAssign => Opcode.DivFloor,
                TokenType.RemainderAssign => Opcode.Rem,
                TokenType.ModuloAssign => Opcode.Mod,
                TokenType.BitLeftShiftAssign => Opcode.BitShl,
                TokenType.BitRightShiftAssign => Opcode.BitShr,
                TokenType.BitLeftRotateAssign => Opcode.BitRol,
                TokenType.BitRightRotateAssign => Opcode.BitRor,
                TokenType.BitAndAssign => Opcode.BitAnd,
                TokenType.BitOrAssign => Opcode.BitOr,
                TokenType.BitXorAssign => Opcode.BitXor,
                TokenType.Assign => Opcode.Move,
                _ => Opcode.None
            };

            // Equality
            var cmpOpcode = expression.Operator switch
            {
                TokenType.Equals => Opcode.JumpIfF,
                TokenType.NotEquals => Opcode.JumpIfT,
                TokenType.LessThan => Opcode.JumpIfGTE,
                TokenType.LessThanOrEquals => Opcode.JumpIfGT,
                TokenType.MoreThan => Opcode.JumpIfLTE,
                TokenType.MoreThanOrEquals => Opcode.JumpIfLT,
                _ => Opcode.None
            };

            // Conditionals
            var cndOpcode = expression.Operator switch
            {
                TokenType.ConditionalAnd => Opcode.CondAnd,
                TokenType.ConditionalOr => Opcode.CondOr,
                _ => Opcode.None
            };

            // Arithmetic
            var artOpcode = expression.Operator switch
            {
                TokenType.Add => Opcode.Add,
                TokenType.Subtract => Opcode.Sub,
                TokenType.Multiply => Opcode.Mul,
                TokenType.Exponent => Opcode.Pow,
                TokenType.Divide => Opcode.Div,
                TokenType.DivideCeil => Opcode.DivCeil,
                TokenType.DivideFloor => Opcode.DivFloor,
                TokenType.Remainder => Opcode.Rem,
                TokenType.Modulo => Opcode.Mod,
                TokenType.BitLeftShift => Opcode.BitShl,
                TokenType.BitRightShift => Opcode.BitShr,
                TokenType.BitLeftRotate => Opcode.BitRol,
                TokenType.BitRightRotate => Opcode.BitRor,
                TokenType.BitAnd => Opcode.BitAnd,
                TokenType.BitOr => Opcode.BitOr,
                TokenType.BitXor => Opcode.BitXor,
                _ => Opcode.None
            };

            if (expression.Operator == TokenType.Dot) // Dereference a.b
            {
                // TODO Clean this up
                var prevAsnSrc = AssignmentRHS;
                AssignmentRHS = null;
                var lhs = expression.Left.Accept(this);
                AssignmentRHS = prevAsnSrc;
                BinOpLHS = lhs;

                if (lhs != null) contextStack.Push((IContext)lhs.Type.Tag);
                var rhs = expression.Right.Accept(this);
                if (lhs != null) contextStack.Pop();
                lhs?.Free();

                BinOpLHS = null;
                return rhs;
            }
            else if (expression.Operator == TokenType.BitPack) // BitPack <<|
            {
                var a = expression.Left.Accept(this);
                var b = expression.Right.Accept(this);
                var c = CurrentFunction.AllocateStorage(a.Type);

                int shlValue = b.Type.Size;

                CurrentFunction.Body.Emit(Opcode.BitShl, c.Operand, a.Operand, new Operand { Type = OperandType.ImmediateUnsigned, Value = shlValue });
                CurrentFunction.Body.Emit(Opcode.BitOr, c.Operand, a.Operand, b.Operand);
                
                b.Free();
                a.Free();

                return c;
            }
            else if (expression.Operator is TokenType.Range or TokenType.RangeInclusive
                                         or TokenType.RangeLength or TokenType.RangeTerminal)
            {
                // TODO Should be allocateable on a Register
                var tdeRangeTuple = rootModule.TupleTypes.First(x => x.Name == "Range");
                var typeRangeTuple = new CobType(eCobType.Tuple, tag: tdeRangeTuple);
                var local = CurrentFunction.AllocateLocal(new CobVariable("$tuple", typeRangeTuple, false)
                {
                    StructValue = tdeRangeTuple.Fields.Select(x => new CobVariable(x.Name, x.Type, true)).ToArray()
                });
                var c = CurrentFunction.AllocateStorage(typeRangeTuple, local);

                var a = expression.Left.Accept(this);
                var b = expression.Right.Accept(this);

                if (expression.Operator == TokenType.Range)
                {
                    var d = CurrentFunction.AllocateStorage(b.Type);
                    CurrentFunction.Body.Emit(Opcode.Sub, d.Operand, b.Operand, new Operand { Type = OperandType.ImmediateUnsigned, Value = 1 });
                    b.Free();
                    b = d;
                }
                else if (expression.Operator == TokenType.RangeLength)
                {
                    var d = CurrentFunction.AllocateStorage(b.Type);
                    CurrentFunction.Body.Emit(Opcode.Add, d.Operand, a.Operand, b.Operand);
                    b.Free();
                    b = d;
                }
                else if (expression.Operator == TokenType.RangeTerminal)
                {
                    var d = CurrentFunction.AllocateStorage(b.Type);
                    CurrentFunction.Body.Emit(Opcode.BitNot, d.Operand, b.Operand);
                    b.Free();
                    b = d;
                }

                CurrentFunction.Body.Emit(Opcode.SetField, c.Operand, new Operand { Type = OperandType.ImmediateUnsigned, Value = 0 }, a.Operand);
                CurrentFunction.Body.Emit(Opcode.SetField, c.Operand, new Operand { Type = OperandType.ImmediateUnsigned, Value = 1 }, b.Operand);

                b.Free();
                a.Free();

                return c;
            }
            else if (cndOpcode != Opcode.None) // Conditional && ||
            {
                var a = expression.Left.Accept(this);
                var b = expression.Right.Accept(this);
                var c = CurrentFunction.AllocateStorage(CobType.Boolean);

                CurrentFunction.Body.Emit(cndOpcode, c.Operand, a.Operand, b.Operand);

                b.Free();
                a.Free();

                return c;
            }
            else if (cmpOpcode != Opcode.None) // Equality == != < > <= >=
            {
                var a = expression.Left.Accept(this);
                var b = expression.Right.Accept(this);

                var c = CurrentFunction.AllocateStorage(CobType.Boolean);

                CurrentFunction.Body.Emit(Opcode.Compare, c.Operand, a.Operand, b.Operand);

                if (conditionalStack == 0)
                {
                    var elseLabel = CurrentFunction.Body.AllocateLabel();
                    var endLabel = CurrentFunction.Body.AllocateLabel();

                    CurrentFunction.Body.Emit(cmpOpcode, c.Operand, elseLabel);
                    CurrentFunction.Body.Emit(Opcode.Move, c.Operand, new Operand { Type = OperandType.ImmediateUnsigned, Value = 1 });
                    CurrentFunction.Body.Emit(Opcode.Jump, endLabel);
                    elseLabel.Mark();
                    CurrentFunction.Body.Emit(Opcode.Move, c.Operand, new Operand { Type = OperandType.ImmediateUnsigned, Value = 0 });
                    endLabel.Mark();
                }

                b.Free();
                a.Free();

                return c;
            }
            else if (artOpcode != Opcode.None) // Arithmetic + - * /
            {
                var a = expression.Left.Accept(this);
                var b = expression.Right.Accept(this);

                var c = CurrentFunction.AllocateStorage(a.Type);
                
                CurrentFunction.Body.Emit(artOpcode, c.Operand, a.Operand, b.Operand);
                
                b.Free();
                a.Free();

                return c;
            }
            else if (asnOpcode != Opcode.None) // Assignment = += -=
            {
                Storage? c;
                var b = expression.Right.Accept(this);

                if (asnOpcode != Opcode.Move)
                {
                    // TODO Can be rewritten
                    c = expression.Left.Accept(this);
                    CurrentFunction.Body.Emit(asnOpcode, c.Operand, c.Operand, b.Operand);
                    AssignmentRHS = c;
                }
                else
                {
                    AssignmentRHS = b;
                    c = null;
                }

                var a = expression.Left.Accept(this);

                a?.Free();
                c?.Free();
                b?.Free();

                AssignmentRHS = null;

                return null;
            }
            else
                throw new ArgumentOutOfRangeException(nameof(expression));
        }

        public Storage? Visit(PrefixOperatorExpression expression)
        {
            var right = expression.Right.Accept(this);
            var c = CurrentFunction.AllocateStorage(right.Type);

            // TODO Support rhs logical !
            if (expression.Operator == TokenType.Not && conditionalStack != 0) // Logical !
            {
                CurrentFunction.Body.Emit(Opcode.Not, c.Operand, right.Operand);
                CurrentFunction.Body.Emit(Opcode.Compare, c.Operand, c.Operand, new Operand { Type = OperandType.ImmediateUnsigned, Value = 1 });
            }
            else
            {
                var opcode = expression.Operator switch
                {
                    TokenType.Not => Opcode.BitNot,
                    TokenType.Subtract => Opcode.Neg,
                    _ => throw new ArgumentOutOfRangeException(nameof(expression))
                };

                CurrentFunction.Body.Emit(opcode, c.Operand, right.Operand);
            }

            right.Free();

            return c;
        }

        public Storage? Visit(BlockExpression expression)
        {
            for (var i = 0; i < expression.Expressions.Count; i++)
            {
                var expr = expression.Expressions[i];
                var retStorage = expr.Accept(this);
                if (expr is FatArrowExpression)
                    return retStorage;

                retStorage?.Free();
            }

            return null;
        }

        public Storage? Visit(CallExpression expression)
        {
            // CAST OPERATOR
            var castType = VisitCast(expression);
            if (castType != null)
                return castType;

            // FUNCTION IDENTIFIER
            var functionStorage = expression.FunctionExpression.Accept(this);
            
            // TUPLE ALLOCATION OPERATOR
            var tuple = VisitTupleAllocation(expression, functionStorage);
            if (tuple != null)
                return tuple;

            var function = functionStorage?.Type.TagFunction;
            if (function == null)
            {
                Messages.Add(Message.CannotCallType, expression, functionStorage?.Type.ToString() ?? "(null)");
                return null;
            }

            var retStorage = function.ReturnType != eCobType.None
                           ? CurrentFunction.AllocateStorage(function.ReturnType)
                           : null;

            // ARGUMENTS
            var parameters = function.Parameters;
            var arguments = expression.Arguments;
            if (function.CallingConvention == CallingConvention.ThisCall)
            {
                var lArguments = new List<Expression>(arguments);
                lArguments.Insert(0, new IdentifierExpression(new Token(TokenType.Identifier, "range", "", 0, 0)));
                arguments = lArguments;
            }
            var hasSpreadParameter = parameters.Count > 0 && parameters[^1].IsSpread;
            if (arguments.Count != parameters.Count && !hasSpreadParameter)
                Messages.Add(Message.FunctionParameterCountMismatch, expression, parameters.Count, arguments.Count);
            
            IReadOnlyList<Operand>? operandArguments;
            if (arguments.Count > 0)
            {
                var aOperandArguments = new Operand[arguments.Count];
                for (int i = 0; i < arguments.Count; ++i)
                {
                    var paramType = parameters.ElementAtOrDefault(i);
                    var argStorage = arguments[i].Accept(this);

                    if (paramType != null && paramType.IsSpread) paramType = null;
                    if (argStorage == null
                    ||  (paramType != null && !CobType.IsCastable(argStorage.Type, paramType.Type)))
                    {
                        aOperandArguments[i] = Operand.None;
                        Messages.Add(Message.TypeMismatch, arguments[i], paramType?.Type, argStorage);
                        continue;
                    }
                    
                    if (paramType != null && argStorage.Type != paramType.Type)
                        argStorage = EmitCast(argStorage, paramType.Type);

                    aOperandArguments[i] = argStorage.Operand;
                }

                operandArguments = aOperandArguments;
            }
            else
                operandArguments = null;
            
            // CALL
            CurrentFunction.Body.Emit(Opcode.Call, functionStorage.Operand, retStorage?.Operand, operandArguments);

            // CLEANUP
            functionStorage.Free(); // function reg
            if (operandArguments != null)
            {
                for (int i = 0; i < operandArguments.Count; ++i) // argument regs
                    CurrentFunction.FreeStorage(operandArguments[i]);
            }

            return retStorage;
        }

        private Storage? VisitCast(CallExpression expression)
        {
            if (expression.FunctionExpression is not IdentifierExpression ie)
                return null;
            
            if (!CobType.TryParse(ie.Value, out var castType)
            ||  castType.Type == eCobType.Tuple
            ||  castType.Type == eCobType.Struct
            ||  castType.Type == eCobType.None)
                return null;

            var arguments = expression.Arguments;
            if (arguments.Count != 1)
            {
                Messages.Add(Message.FunctionParameterCountMismatch, expression, 1, arguments.Count);
                return null;
            }

            var source = arguments[0].Accept(this);
            var target = EmitCast(source, castType);

            return target;
        }

        private Storage EmitCast(Storage source, CobType dstType)
        {
            var srcType = source.Type;
            if (srcType == dstType)
                return source;
            
            if (srcType == eCobType.Unsigned
            ||  srcType == eCobType.Signed
            ||  srcType == eCobType.Float)
            {
                return source with { Type = dstType };
            }
            else
                throw new NotImplementedException();
        }

        private Storage? VisitTupleAllocation(CallExpression expression, Storage? functionStorage)
        {
            if (functionStorage == null || functionStorage.Type != eCobType.Tuple
            ||  functionStorage.Type.Tag is not TupleDefinitionExpression tde)
                return null;

            functionStorage.Free();
            
            // TODO Should be allocateable on a Register
            var local = CurrentFunction.AllocateLocal(new CobVariable("$tuple", functionStorage.Type, false)
            {
                StructValue = tde.Fields.Select(x => new CobVariable(x.Name, x.Type, true)).ToArray()
            });

            for (var i = 0; i < expression.Arguments.Count; ++i)
            {
                var argStorage = expression.Arguments[i].Accept(this);
                CurrentFunction.Body.Emit(
                    Opcode.SetField,
                    new Operand { Type = OperandType.Local, Value = local },
                    new Operand { Type = OperandType.ImmediateUnsigned, Value = i },
                    argStorage.Operand
                );
            }

            return CurrentFunction.AllocateStorage(functionStorage.Type, local);
        }

        public Storage? Visit(StructInitializerExpression expression)
        {
            var structTypeStorage = expression.StructTypeExpression.Accept(this);
            if (structTypeStorage == null || structTypeStorage.Type != eCobType.Struct
            ||  structTypeStorage.Type.Tag is not StructDefinitionExpression sde)
            {
                Messages.Add(Message.CannotInstantiateType, expression.StructTypeExpression, structTypeStorage?.Type.ToString() ?? "(null)");
                return null;
            }
            
            var local = CurrentFunction.AllocateLocal(new CobVariable("$struct", structTypeStorage.Type, false)
            {
                StructValue = sde.Fields.Select(x => new CobVariable(x.Name, x.Type, true)).ToArray()
            });

            var storage = CurrentFunction.AllocateStorage(structTypeStorage.Type, local);

            if (expression.Assignments != null)
            {
                BinOpLHS = storage;
                contextStack.Push(sde);
                for (var i = 0; i < expression.Assignments.Count; ++i)
                    expression.Assignments[i].Accept(this);
                contextStack.Pop();
                BinOpLHS = null;
            }

            return storage;
        }
        
        public Storage? Visit(IdentifierExpression expression)
        {
            if (AssignmentRHS != null)
            {
                CurrentContext.SetIdentifier(this, expression);

                //Messages.Add(Message.UndeclaredIdentifier, expression, expression.Value);
                return null;
            }
            else
            {
                var storage = CurrentContext.GetIdentifier(this, expression);
                if (storage == null)
                    Messages.Add(Message.UndeclaredIdentifier, expression, expression.Value);

                return storage;
            }
        }

        public Storage? Visit(LensExpression expression)
        {
            var target = expression.Expression.Accept(this);

            var storage = CurrentFunction.AllocateStorage(new CobType(eCobType.Lens, elementType: expression.ElementType));
            CurrentFunction.Body.Emit(Opcode.Lens, storage.Operand, target.Operand);
            target.Free();

            return storage;
        }

        public Storage? Visit(ArrayExpression expression)
        {
            Storage? storage = null;

            var source = expression.Left.Accept(this);
            var index = expression.Index.Accept(this);

            if (source == null)
                Messages.Add(Message.CannotIndexType, expression, "none");
            else if (source.Type == eCobType.Struct && source.Type.Tag is StructDefinitionExpression sde
            &&  sde.Indexer != null)
            {
                sde.Indexer.Index = index;
                BinOpLHS = source; // ???
                contextStack.Push(sde.Indexer);
                storage = sde.Indexer.GetterExpression.Accept(this);
                contextStack.Pop();
                BinOpLHS = null;
                sde.Indexer.Index = null;
            }
            else if (source.Type == eCobType.Lens)
            {
                storage = CurrentFunction.AllocateStorage(source.Type.ElementType);
                CurrentFunction.Body.Emit(
                    Opcode.GetElem,
                    storage.Operand,
                    source.Operand,
                    index.Operand
                );
            }
            else
                Messages.Add(Message.CannotIndexType, expression, source.Type);

            index?.Free();
            source?.Free();

            return storage;
        }

        public Storage? Visit(NumberExpression expression)
        {
            return CurrentFunction.AllocateStorage(
                new CobType(expression.Type, expression.BitSize),
                expression.LongValue
            );
        }

        public Storage? Visit(BooleanExpression expression)
        {
            return CurrentFunction.AllocateStorage(CobType.Boolean, expression.Value ? 1 : 0);
        }

        public Storage? Visit(StringExpression expression)
        {
            var byteCount = Encoding.UTF8.GetByteCount(expression.Value);
            var data = new byte[byteCount + 1];
            Encoding.UTF8.GetBytes(expression.Value, 0, expression.Value.Length, data, 0);
            
            var global = AllocateGlobal(new CobVariable(
                $"string{Globals.Count}",
                CobType.String,
                false
            ) { BufferValue = data });
            
            // TODO AllocateStorage on Global "function"??
            return new Storage(
                CurrentFunction,
                new Operand
                {
                    Type = OperandType.Global,
                    Value = global,
                    Size = -1
                },
                CobType.String
            );
        }

        public Storage? Visit(EmptyExpression expression)
        {
            return null;
        }

        [Obsolete]
        public int FindGlobal(string name)
        {
            return Globals.FindIndex(x => x.Name == name);
        }

        public int FindGlobal(CobVariable variable)
        {
            return Globals.FindIndex(x => x == variable);
        }
        
        public int AllocateGlobal(CobVariable variable)
        {
            if (variable == null) throw new ArgumentNullException(nameof(variable));

            if (FindGlobal(variable) != -1)
                return -1;

            var idx = Globals.Count;
            Globals.Add(variable);

            CurrentModule.Variables[variable.Name] = variable;

            return idx;
        }

        public bool IsSymbolVisible(CobVariable variable)
        {
            if (CurrentFunction != null && CurrentFunction.Module == CurrentModule)
                return true;

            return variable.Name.Length > 0 && char.IsUpper(variable.Name[0]);
        }

        public void ValidateVariableAccess(CobVariable? variable, IdentifierExpression expression)
        {
            if (variable == null)
                Messages.Add(Message.UndeclaredIdentifier, expression, expression.Value);
            else if (!IsSymbolVisible(variable))
                Messages.Add(Message.CannotAccessPrivateSymbol, expression, expression.Value, CurrentModule.Name ?? "(root)");
            else if (!variable.Mutable)
                Messages.Add(Message.IllegalAssignmentImmutable, expression);
        }

        public static Compiler Compile(ScriptExpression ast, MessageCollection messages)
        {
            try
            {
                stopwatch.Start();
                var compiler = new Compiler(messages);
                ast.Accept(compiler);
                return compiler;
            }
            finally
            {
                stopwatch.Stop();
            }
        }

        public static long TotalMilliseconds => stopwatch.ElapsedMilliseconds;
        private static readonly Stopwatch stopwatch = new ();
    }

    internal sealed record Import
    {
        public string Library { get; init; }

        public string? SymbolName { get; init; }

        public Function? Function { get; init; }
    }

    internal sealed record Export
    {
        public Function Function { get; init; }
    }

    internal interface IContext
    {
        Storage? GetIdentifier(Compiler compiler, IdentifierExpression expression);

        void SetIdentifier(Compiler compiler, IdentifierExpression expression);
    }
}
