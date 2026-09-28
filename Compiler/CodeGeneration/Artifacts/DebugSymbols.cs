using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Compiler.CodeGeneration.Artifacts
{
    internal sealed class DebugSymbols
    {

    }

    internal sealed record DebugSymbol(Function Parent, int IP, string Filename, int Line);
}
