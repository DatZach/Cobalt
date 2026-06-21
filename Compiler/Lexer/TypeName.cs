using Compiler.CodeGeneration.Artifacts;

namespace Compiler.Lexer
{
    // Identifier (u16, any, nil, UserDefinedType, ...)
    // Function Signature
    // Record Signature
    // Union
    // TODO struct?
    internal class TypeName
    {
        public static readonly TypeName Any = new() { Type = eTypeName.Identifier, Identifier = "any" };

        public eTypeName Type { get; init; }

        public string? Identifier { get; init; }

        public FunctionSignature? Function { get; init; }

        public RecordSignature? Record { get; init; }

        public TypeName? Union { get; init; }

        public IReadOnlyList<TypeName>? Generic { get; init; } // [Name]`T1`T2

        public bool IsArray { get; init; }

        public bool IsErrorable { get; init; }

        public bool IsNillable { get; init; }

        public class FunctionSignature
        {
            public IReadOnlyList<Parameter>? Parameters { get; init; }

            public TypeName? ReturnType { get; init; }

            public class Parameter
            {
                public string Name { get; init; }

                public TypeName TypeName { get; init; }

                public bool IsSpread { get; init; }
            }
        }

        public class RecordSignature
        {
            private static int uniqueId;
            public int UniqueId { get; } = uniqueId++;

            public eRecordType Type { get; init; }

            public IReadOnlyList<Field>? Fields { get; init; }

            // TODO The rest...

            public class Field
            {
                public string? Name { get; init; }

                public TypeName TypeName { get; init; }
            }
        }
    }

    internal enum eTypeName
    {
        None,
        Identifier,
        FunctionSignature,
        RecordSignature
    }
}
