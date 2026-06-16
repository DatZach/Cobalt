using System.Diagnostics;

namespace Compiler.CodeGeneration.Artifacts
{
    [DebuggerDisplay("Distinct '{Name}'")]
    internal sealed class DistinctType
    {
        public string Name { get; }

        public CobType SubType { get; }

        public DistinctType(string name, CobType subType)
        {
            Name = name;
            SubType = subType;
        }
    }
}
