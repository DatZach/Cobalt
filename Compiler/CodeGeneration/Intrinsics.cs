using Compiler.CodeGeneration.Artifacts;

namespace Compiler.CodeGeneration
{
    internal static class Intrinsics
    {
        public static RecordType Range { get; private set; } = null!;

        public static RecordType Lens { get; private set; } = null!;

        //public static StructType Array { get; private set; } = null!;

        public static RecordType Array { get; set; } = null!;

        public static void InitializeForPass0(Compiler compiler)
        {
            
        }

        public static void InitializeForPass1(Compiler compiler)
        {
            //var stdMod = compiler.RootModule.FindModule("Standard") ?? throw new Exception("The module 'Standard' is not defined by the Standard Library");
            var stdMod = compiler.RootModule;
            Range = stdMod.FindRecordType("Range") ?? throw new Exception("The symbol 'Range' is not defined by the Standard Library");
            Lens = stdMod.FindRecordType("Lens") ?? throw new Exception("The symbol 'Lens' is not defined by the Standard Library");
            //Array = compiler.RootModule.FindStructType("Array") ?? throw new Exception("The symbol 'Array' is not defined by the Standard Library");
        }

        public static RecordType FindOrAllocateAnonymousTupleType(IReadOnlyList<CobType> fieldTypes, Compiler compiler)
        {
            var typeName = $"AnonTuple'{string.Join('\'', fieldTypes.Select(x => x.ToString()))}";
            var recordType = compiler.RootModule.FindRecordType(typeName);
            if (recordType == null)
            {
                recordType = compiler.RootModule.AllocateRecordType(typeName, eRecordType.Tuple);
                foreach (var fieldType in fieldTypes)
                    recordType.AllocateField(string.Empty, fieldType, false, false);
            }

            return recordType;
        }
    }
}
