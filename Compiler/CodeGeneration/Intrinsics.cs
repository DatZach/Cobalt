using Compiler.CodeGeneration.Artifacts;
using System.Text;

namespace Compiler.CodeGeneration
{
    internal static class Intrinsics
    {
        public static CobType Range { get; private set; } = null!;

        public static CobType Lens { get; private set; } = null!;

        public static CobType Array { get; set; } = null!;

        public static CobType StringType { get; set; } = null!;

        public static void InitializeForPass0(Compiler compiler)
        {
            
        }

        public static void InitializeForPass1(Compiler compiler)
        {
            //var stdMod = compiler.RootModule.FindModule("Standard") ?? throw new Exception("The module 'Standard' is not defined by the Standard Library");
            var stdMod = compiler.RootModule;
            Range = stdMod.FindDistinctType("Range") ?? throw new Exception("The symbol 'Range' is not defined by the Standard Library");
            Lens = stdMod.FindDistinctType("Lens") ?? throw new Exception("The symbol 'Lens' is not defined by the Standard Library");
            //Array = compiler.RootModule.FindStructType("Array") ?? throw new Exception("The symbol 'Array' is not defined by the Standard Library");
            StringType = stdMod.FindDistinctType("string") ?? throw new Exception("The symbol 'string' is not defined by the Standard Library");
        }

        public static RecordType FindOrAllocateAnonymousTupleType(IReadOnlyList<CobType> fieldTypes, Compiler compiler)
        {
            var typeName = $"AnonTuple'{string.Join('\'', fieldTypes.Select(x => x.ToString()))}";
            var recordType = compiler.RootModule.FindRecordType(typeName);
            if (recordType == null)
            {
                recordType = compiler.RootModule.AllocateRecordType(typeName, eRecordType.Tuple);
                for (var i = 0; i < fieldTypes.Count; ++i)
                {
                    var fieldType = fieldTypes[i];
                    recordType.AllocateField($"'{i}", fieldType, true, false, false);
                }
            }

            return recordType;
        }

        public static Variable AllocateStringLiteral(string value, Compiler? compiler)
        {
            var byteCount = Encoding.UTF8.GetByteCount(value);
            var data = new byte[byteCount + 1];
            Encoding.UTF8.GetBytes(value, 0, value.Length, data, 0);

            var global = compiler != null
                       ? compiler.RootModule.AllocateGlobal($"$string{compiler.Globals.Count}", CobType.String, false)
                       : new Variable("", CobType.String, false);
            global.RecordValue = new[]
            {
                new Variable("Address", CobType.UInt, false, data),
                new Variable("Length", CobType.UInt, false, byteCount)
            };

            return global;
        }
    }
}
