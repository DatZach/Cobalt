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

    internal sealed record DebugSymbol(string Name, object Value);

    internal interface IDebugSymbolProvider
    {
        IEnumerable<ISymbol> Symbols { get; } // TODO Fix this signature, should not box...
    }
}
