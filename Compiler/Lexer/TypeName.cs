using System.Text;
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

        public IReadOnlyList<TypeName>? Generic { get; init; } // [Name]`T1`T2

        public TypeName? Union { get; init; }

        public bool IsArray { get; init; }

        public bool IsErrorable { get; init; }

        public bool IsNillable { get; init; }

        public override string ToString()
        {
            var sb = new StringBuilder();
            var type = this;
            while (type != null)
            {
                switch (Type)
                {
                    case eTypeName.Identifier:
                        sb.Append(Identifier);
                        break;

                    case eTypeName.FunctionSignature:
                        sb.Append("function(");
                        for (var i = 0; i < Function.Parameters.Count; ++i)
                        {
                            var x = Function.Parameters[i];
                            if (x.IsSpread) sb.Append("...");
                            sb.Append(x.TypeName);
                            sb.Append(' ');
                            sb.Append(x.Name);
                            
                            if (i < Function.Parameters.Count - 1)
                                sb.Append(", ");
                        }

                        sb.Append(')');

                        if (Function.ReturnTypeName != null)
                        {
                            sb.Append(' ');
                            sb.Append(Function.ReturnTypeName);
                        }

                        if (Function.CallingConvention != CallingConvention.Default)
                        {
                            sb.Append(", ");
                            sb.Append(Function.CallingConvention);
                        }
                        break;

                    case eTypeName.RecordSignature:
                        sb.Append("record (TODO)");
                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (IsArray)
                    sb.Append("[]");
                if (IsNillable)
                    sb.Append('?');
                if (IsErrorable)
                    sb.Append('!');

                type = type.Union;
                if (type != null)
                    sb.Append(" | ");
            }

            return sb.ToString();
        }

        public class FunctionSignature
        {
            public IReadOnlyList<Parameter>? Parameters { get; init; }

            public TypeName? ReturnTypeName { get; init; }

            public CallingConvention CallingConvention { get; init; }

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
