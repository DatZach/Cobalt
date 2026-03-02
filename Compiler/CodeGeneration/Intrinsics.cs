using Compiler.CodeGeneration.Artifacts;

namespace Compiler.CodeGeneration
{
    internal static class Intrinsics
    {
        public static TupleType Range { get; private set; } = null!;

        public static TupleType Lens { get; private set; } = null!;

        //public static StructType Array { get; private set; } = null!;

        public static StructType Array { get; set; } = null!;

        public static void InitializeForPass0(Compiler compiler)
        {
            StringContext.Instance.Compiler = compiler;
        }

        public static void InitializeForPass1(Compiler compiler)
        {
            Range = compiler.RootModule.FindTupleType("Range") ?? throw new Exception("The symbol 'Range' is not defined by the Standard Library");
            Lens = compiler.RootModule.FindTupleType("Lens") ?? throw new Exception("The symbol 'Lens' is not defined by the Standard Library");
            //Array = compiler.RootModule.FindStructType("Array") ?? throw new Exception("The symbol 'Array' is not defined by the Standard Library");
        }
    }
}
