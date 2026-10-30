using Compiler.Ast.Expressions.Statements;
using Compiler.CodeGeneration.Artifacts;
using System.Text;

namespace Compiler.Lexer
{
    // Identifier (u16, any, nil, UserDefinedType, ...)
    // Function Signature
    // Record Signature
    // Union
    // TODO struct?
    // TODO StartToken, EndToken
    internal class TypeName
    {
        public static readonly TypeName None = new() { Type = eTypeName.None };
        public static readonly TypeName Any = new() { Type = eTypeName.Identifier, Identifier = "any" };

        public eTypeName Type { get; init; }

        public string? Identifier { get; init; }

        public FunctionSignature? Function { get; init; }

        public RecordStatement? Record { get; init; } // TODO Rename type to RecordSignature

        public IReadOnlyList<TypeName>? Generic { get; init; } // [Name]`T1`T2

        public TypeName? Union { get; init; }

        public bool IsArray { get; init; }

        public bool IsErrorable { get; init; }

        public bool IsNillable { get; init; }

        public bool IsConst { get; init; }

        public static TypeName FromIdentifier(string name) => new() { Type = eTypeName.Identifier, Identifier = name };

        public override string ToString()
        {
            var sb = new StringBuilder();
            var type = this;
            while (type != null)
            {
                switch (type.Type)
                {
                    case eTypeName.Identifier:
                        sb.Append(type.Identifier);
                        break;

                    case eTypeName.FunctionSignature:
                        sb.Append("function(");
                        for (var i = 0; i < type.Function.Parameters.Count; ++i)
                        {
                            var x = type.Function.Parameters[i];
                            if (x.IsSpread) sb.Append("...");
                            sb.Append(x.TypeName);
                            sb.Append(' ');
                            sb.Append(x.Name);
                            
                            if (i < type.Function.Parameters.Count - 1)
                                sb.Append(", ");
                        }

                        sb.Append(')');

                        if (type.Function.ReturnTypeName != null)
                        {
                            sb.Append(' ');
                            sb.Append(type.Function.ReturnTypeName);
                        }

                        if (type.Function.CallingConvention != CallingConvention.Default)
                        {
                            sb.Append(", ");
                            sb.Append(type.Function.CallingConvention);
                        }
                        break;

                    case eTypeName.RecordSignature:
                        sb.Append(type.Record.Name);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (type.Generic != null)
                {
                    foreach (var generic in type.Generic)
                    {
                        sb.Append('`');
                        sb.Append(generic);
                    }
                }

                if (type.IsArray)
                    sb.Append("[]");
                if (type.IsNillable)
                    sb.Append('?');
                if (type.IsErrorable)
                    sb.Append('!');

                type = type.Union;
                if (type != null)
                    sb.Append(" | ");
            }

            if (IsConst)
                sb.Append('^');

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
    }

    internal enum eTypeName
    {
        None,
        Identifier,
        FunctionSignature,
        RecordSignature
    }
}
