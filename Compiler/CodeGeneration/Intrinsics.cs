using Compiler.CodeGeneration.Artifacts;

namespace Compiler.CodeGeneration
{
    internal static class Intrinsics
    {
        public static TupleType Range { get; private set; } = null!;

        public static void InitializeForPass0(Compiler compiler)
        {
            StringContext.Instance.Compiler = compiler;
        }

        public static void InitializeForPass1(Compiler compiler)
        {
            Range = compiler.RootModule.FindTupleType("Range") ?? throw new Exception("The symbol 'Range' is not defined by the Standard Library");
        }
    }
}
