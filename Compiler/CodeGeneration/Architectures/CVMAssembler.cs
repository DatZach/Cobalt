using Compiler.Ast.Expressions.Statements;
using Compiler.CodeGeneration.Artifacts;
using Compiler.Utility;
using System.Collections;
using System.Reflection;
using Compiler.Lexer;
using Module = Compiler.CodeGeneration.Artifacts.Module;

namespace Compiler.CodeGeneration.Architectures
{
    internal sealed class CVMAssembler : ArtifactAssembler
    {
        public override IReadOnlyList<string> SupportedArchitectures => new[] { "cvm" };

        public override string DefaultExtension => "cia";

        private Artifact artifact;
        
        public override void Assemble(Artifact artifact, ArtifactDirective directive, string outputFilename)
        {
            this.artifact = artifact;

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            writer.WriteSection(AsMagic('C', 'V', 'M', 'A'), writer =>
            {
                Serialize(writer, artifact);
            });

            writer.Flush();

            FileSystem.WriteAllBytes(outputFilename, stream.ToArray());
        }

        public void Serialize(BinaryWriter writer, object source)
        {
            if (source is Artifact artifact)
                Serialize(writer, artifact);
            else if (source is Import import)
                Serialize(writer, import);
            else if (source is Export export)
                Serialize(writer, export);
            else if (source is Module module)
                Serialize(writer, module);
            else if (source is TraitType traitType)
                Serialize(writer, traitType);
            else if (source is RecordType recordType)
                Serialize(writer, recordType);
            else if (source is Function function)
                Serialize(writer, function);
            else if (source is Function.Parameter parameter)
                Serialize(writer, parameter);
            else if (source is Field field)
                Serialize(writer, field);
            else if (source is Variable variable)
                Serialize(writer, variable);
            else if (source is CobType cobType)
                Serialize(writer, cobType);
            else
                throw new ArgumentOutOfRangeException($"Unable to serialize type '{source?.GetType()}'");
        }

        private void Serialize(BinaryWriter writer, Artifact artifact)
        {
            const int Version = 1;

            writer.Write((ushort)Version);
            writer.Write(AsFlags());
            writer.Write(AsFlags());
            writer.Write(DateTime.UtcNow.ToCobaltTime());
            writer.Write(Guid.NewGuid().ToByteArray());

            writer.WriteSection(AsMagic('I', 'P', 'R', 'T'), writer => { writer.WriteList(this, artifact.Imports); });
            writer.WriteSection(AsMagic('E', 'P', 'R', 'T'), writer => { writer.WriteList(this, artifact.Exports); });
            writer.WriteSection(AsMagic('M', 'O', 'D', 'U'), writer => { writer.WriteList(this, artifact.Modules); });
            writer.WriteSection(AsMagic('T', 'R', 'A', 'I'), writer => { writer.WriteList(this, artifact.TraitTypes); });
            writer.WriteSection(AsMagic('R', 'E', 'C', 'D'), writer => { writer.WriteList(this, artifact.RecordTypes); });
            writer.WriteSection(AsMagic('F', 'U', 'N', 'C'), writer => { writer.WriteList(this, artifact.Functions); });
            writer.WriteSection(AsMagic('G', 'L', 'O', 'B'), writer => { writer.WriteList(this, artifact.Globals); });
            writer.WriteSection(AsMagic('T', 'Y', 'P', 'E'), writer => { writer.WriteList(this, artifact.Types); });
            writer.WriteSection(AsMagic('M', 'A', 'I', 'N'), writer =>
            {
                if (artifact.EntryFunction == null)
                    return;

                writer.Write7BitEncodedInt(artifact.Functions.IndexOf(artifact.EntryFunction));
            });
        }

        private void Serialize(BinaryWriter writer, Import import)
        {
            writer.Write(AsFlags(import.SymbolName != null, import.Function != null));
            writer.Write(import.Library);
            if (import.SymbolName != null)
                writer.Write(import.SymbolName);
            //if (Function != null)
            //    writer.Write7BitEncodedInt(artifact.Function.Serialize(writer, artifact));
        }

        private void Serialize(BinaryWriter writer, Export export)
        {
            writer.Write(artifact.Functions.IndexOf(export.Function));
        }

        private void Serialize(BinaryWriter writer, Module module)
        {
            var modules = FieldOf<IReadOnlyList<object>>(module, "modules");
            var traitTypes = FieldOf<IReadOnlyList<object>>(module, "traitTypes");
            var recordTypes = FieldOf<IReadOnlyList<object>>(module, "recordTypes");
            var functions = FieldOf<IReadOnlyList<object>>(module, "functions");
            var globals = FieldOf<IReadOnlyList<object>>(module, "globals");

            writer.Write(AsFlags(module.Parent != null)); // TODO Flag lists
            writer.Write(module.Name);
            writer.Write7BitEncodedInt(artifact.Functions.IndexOf(module.InitializerFunction));
            writer.WriteLookupList(modules, artifact.Modules);
            writer.WriteLookupList(traitTypes, artifact.TraitTypes);
            writer.WriteLookupList(recordTypes, artifact.RecordTypes);
            writer.WriteLookupList(functions, artifact.Functions);
            writer.WriteLookupList(globals, artifact.Globals);
        }

        private void Serialize(BinaryWriter writer, TraitType traitType)
        {
            writer.Write(AsFlags(traitType.Parent != null)); // TODO Flag lists
            writer.Write(traitType.Name);
            writer.WriteLookupList(traitType.Functions, artifact.Functions);
        }

        private void Serialize(BinaryWriter writer, RecordType recordType)
        {
            var generics = FieldOf<IReadOnlyList<GenericDefinition>>(recordType, "generics");
            var traits = FieldOf<IReadOnlyList<TraitType>>(recordType, "traits");
            var factories = FieldOf<IReadOnlyList<Function>>(recordType, "factories");
            var functions = FieldOf<IReadOnlyList<Function>>(recordType, "functions");
            var fields = FieldOf<IReadOnlyList<Field>>(recordType, "fields");

            writer.Write(AsFlags(recordType.Indexer != null, generics.Count > 0, traits.Count > 0, factories.Count > 0, functions.Count > 0, fields.Count > 0, recordType.Type == eRecordType.Struct));
            writer.Write(recordType.Name);
            WriteScopeParentIndex(writer, recordType.Parent);

            if (recordType.Indexer != null)
            {
                writer.Write(AsFlags(recordType.Indexer.Getter != null, recordType.Indexer.Setter != null));
                Serialize(writer, recordType.Indexer.KeyType);
                Serialize(writer, recordType.Indexer.ReturnType);
                if (recordType.Indexer.Getter != null)
                    writer.Write(artifact.Functions.IndexOf(recordType.Indexer.Getter));
                if (recordType.Indexer.Setter != null)
                    writer.Write(artifact.Functions.IndexOf(recordType.Indexer.Setter));
            }
            writer.WriteList(generics, x =>
            {
                writer.Write(AsFlags(x.ConstraintTypeName != null));
                writer.Write(x.Name);
                if (x.ConstraintTypeName != null)
                    writer.Write(x.ConstraintTypeName);
            });
            writer.WriteLookupList(traits, artifact.TraitTypes);
            writer.WriteLookupList(factories, artifact.Functions);
            writer.WriteLookupList(functions, artifact.Functions);
            writer.WriteList(this, fields);
        }

        private void Serialize(BinaryWriter writer, Function function)
        {
            writer.Write(AsFlags(function.NativeImport != null));
            writer.Write(function.Name);
            WriteScopeParentIndex(writer, function.Parent);

            writer.Write(function.ClobberedRegisters);
            writer.Write((byte)function.CallingConvention);
            if (function.NativeImport != null)
                Serialize(writer, function.NativeImport);
            writer.WriteList(this, function.Parameters);
            Serialize(writer, function.ReturnType);
            
            writer.Write7BitEncodedInt(function.Body.Instructions.Count);
            foreach (var x in function.Body.Instructions)
            {
                // TODO Improve
                writer.Write((byte)x.Opcode);
                writer.Write(AsFlags(x.A != null, x.B != null, x.C != null, x.D != null));

                if (x.A != null)
                {
                    writer.Write((byte)x.A.Type);
                    writer.Write7BitEncodedInt64(x.A.Value);
                }

                if (x.B != null)
                {
                    writer.Write((byte)x.B.Type);
                    writer.Write7BitEncodedInt64(x.B.Value);
                }

                if (x.C != null)
                {
                    writer.Write((byte)x.C.Type);
                    writer.Write7BitEncodedInt64(x.C.Value);
                }

                if (x.D != null)
                {
                    writer.Write7BitEncodedInt(x.D.Count);
                    foreach (var y in x.D)
                    {
                        writer.Write((byte)y.Type);
                        writer.Write7BitEncodedInt64(y.Value);
                    }
                }
            }
        }

        private void Serialize(BinaryWriter writer, Field field)
        {
            Serialize(writer, (Variable)field);
            writer.Write(AsFlags(field.Getter != null, field.Setter != null));
            if (field.Getter != null)
                writer.Write7BitEncodedInt(artifact.Functions.IndexOf(field.Getter));
            if (field.Setter != null)
                writer.Write7BitEncodedInt(artifact.Functions.IndexOf(field.Setter));
        }

        private void Serialize(BinaryWriter writer, Function.Parameter parameter)
        {
            Serialize(writer, (Variable)parameter);
            writer.Write(parameter.IsSpread);
            // TODO DefaultValue
        }

        private void Serialize(BinaryWriter writer, Variable variable)
        {
            writer.Write(variable.Name);
            Serialize(writer, variable.Type);
            writer.Write(variable.Mutable);
            if (variable.Value is long longValue)
            {
                writer.Write((byte)0);
                writer.Write(longValue);
            }
            else if (variable.Value is byte[] bufferValue)
            {
                writer.Write((byte)1);
                writer.Write7BitEncodedInt(bufferValue.Length);
                writer.Write(bufferValue);
            }
            else if (variable.Value is Variable[] structValue)
            {
                writer.Write((byte)2);
                writer.Write7BitEncodedInt(structValue.Length);
                foreach (var x in structValue)
                    Serialize(writer, x);
            }
        }

        private void Serialize(BinaryWriter writer, CobType cobType)
        {
            try
            {
                writer.Write7BitEncodedInt64(cobType.ToOperand(artifact).Value);
            }
            catch { }
        }

        private void WriteScopeParentIndex(BinaryWriter writer, object value)
        {
            if (value is Function)
            {
                writer.Write((byte)1);
                writer.Write7BitEncodedInt(artifact.Functions.IndexOf((Function)value));
            }
            else if (value is Module)
            {
                writer.Write((byte)2);
                writer.Write7BitEncodedInt(artifact.Modules.IndexOf((Module)value));
            }
            else if (value is RecordType)
            {
                writer.Write((byte)3);
                writer.Write7BitEncodedInt(artifact.RecordTypes.IndexOf((RecordType)value));
            }
            else if (value is TraitType)
            {
                writer.Write((byte)4);
                writer.Write7BitEncodedInt(artifact.TraitTypes.IndexOf((TraitType)value));
            }
            else
                writer.Write((byte)0);
        }

        public static uint AsMagic(char a, char b, char c, char d) =>
            (uint)(((byte)d << 24) | ((byte)c << 16) | ((byte)b << 8) | ((byte)a << 0));

        public static byte AsFlags(bool a = false, bool b = false, bool c = false, bool d = false,
                                   bool e = false, bool f = false, bool g = false, bool h = false) =>
            (byte)((a ? 0x80 : 0) | (b ? 0x40 : 0) | (c ? 0x20 : 0) | (d ? 0x10 : 0) |
                   (e ? 0x08 : 0) | (f ? 0x04 : 0) | (g ? 0x02 : 0) | (h ? 0x01 : 0));

        private static T FieldOf<T>(object source, string name) =>
            (T)source.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(source);
    }

    internal static class BinaryWriterExtensions
    {
        public static void WriteSection(this BinaryWriter writer, uint magic, Action<BinaryWriter> action)
        {
            using var stream2 = new MemoryStream();
            using var writer2 = new BinaryWriter(stream2);

            action(writer2);

            writer2.Flush();

            var buffer = stream2.ToArray();
            if (buffer.Length > 0)
            {
                var length = buffer.Length;
                var padding = 32 - ((length + 8) % 32);
                length += padding;

                writer.Write(magic);
                writer.Write(length);
                writer.Write(buffer);
                for (int i = 0; i < padding; ++i)
                    writer.Write((byte)0);
            }
        }

        public static void WriteList(this BinaryWriter writer, CVMAssembler assembler, IReadOnlyList<object> list)
        {
            if (list.Count == 0)
                return;

            writer.Write7BitEncodedInt(list.Count);
            foreach (var item in list)
                assembler.Serialize(writer, item);
        }

        public static void WriteList<T>(this BinaryWriter writer, IReadOnlyList<T> list, Action<T> action)
        {
            if (list.Count == 0)
                return;

            writer.Write7BitEncodedInt(list.Count);
            foreach (var item in list)
                action(item);
        }

        public static void WriteLookupList(this BinaryWriter writer, IReadOnlyList<object> list, IList from)
        {
            if (list.Count == 0)
                return;

            writer.Write7BitEncodedInt(list.Count);
            foreach (var item in list)
                writer.Write7BitEncodedInt(from.IndexOf(item));
        }

        public static void Write(this BinaryWriter writer, TypeName? typeName)
        {
            if (typeName == null)
                writer.Write((byte)eTypeName.None);
            else
            {
                writer.Write((byte)typeName.Type);
                writer.Write(CVMAssembler.AsFlags(
                    typeName.IsArray,
                    typeName.IsErrorable,
                    typeName.IsNillable,
                    typeName.Generic != null,
                    typeName.Union != null
                ));

                switch (typeName.Type)
                {
                    case eTypeName.Identifier:
                        writer.Write(typeName.Identifier!);
                        break;
                    case eTypeName.FunctionSignature:
                        writer.Write7BitEncodedInt(typeName.Function.Parameters.Count);
                        foreach (var parameter in typeName.Function.Parameters)
                        {
                            writer.Write(parameter.Name);
                            writer.Write(parameter.TypeName);
                            writer.Write(CVMAssembler.AsFlags(parameter.IsSpread));
                        }

                        writer.Write(typeName.Function.ReturnTypeName);
                        break;
                    case eTypeName.RecordSignature:
                        writer.Write7BitEncodedInt(typeName.Record.UniqueId);
                        writer.Write((byte)typeName.Record.Type);
                        writer.Write7BitEncodedInt((byte)typeName.Record.Fields.Count);
                        foreach (var field in typeName.Record.Fields)
                        {
                            writer.Write(CVMAssembler.AsFlags(field.Name != null));
                            if (field.Name != null)
                                writer.Write(field.Name);
                            writer.Write(field.TypeName);
                        }
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (typeName.Generic != null)
                {
                    writer.Write7BitEncodedInt(typeName.Generic.Count);
                    foreach (var generic in typeName.Generic)
                        writer.Write(generic);
                }

                if (typeName.Union != null)
                    writer.Write(typeName.Union);
            }
        }
    }
}
