using Compiler.Lexer;
using Compiler.Ast.Expressions;
using System.Globalization;
using Compiler.CodeGeneration;

namespace Compiler.Ast.Parselets
{
    internal sealed class NumberParselet : IPrefixExpressionParselet
    {
        private readonly static char[] TypeSpecifiers = { 'u', 's', 'f' };

        public Expression Parse(Parser parser, Token token)
        {
            var value = token.Value;
            var hasHexSpecified = false;
            var hasBinSpecified = false;
            var hasDotSpecified = false;
            var type = eCobType.None;
            var bitSize = 32;

            int i = 0;
            if (value[0] == '-' || value[0] == '+')
            {
                type = eCobType.Signed;
                ++i;
            }

            if (value.Length > 2 && value[i] == '0' && value[i + 1] == 'x')
            {
                value = value[2..];
                if (type == eCobType.None)
                    type = eCobType.Unsigned;
                hasHexSpecified = true;
            }
            else if (value.Length > 2 && value[i] == '0' && value[i + 1] == 'b')
            {
                value = value[2..];
                if (type == eCobType.None)
                    type = eCobType.Unsigned;
                hasBinSpecified = true;
            }
            else if (value.IndexOf('.') != -1)
            {
                bitSize = 64;
                type = eCobType.Float;
                hasDotSpecified = true;
            }

            if (value.IndexOf('_') != -1)
                value = value.Replace("_", "");

            int typeIdx = value.IndexOfAny(TypeSpecifiers);
            if (typeIdx != -1)
            {
                type = value[typeIdx] switch
                {
                    'u' => eCobType.Unsigned,
                    's' => eCobType.Signed,
                    'f' => eCobType.Float,
                    _ => eCobType.None
                };

                var isValidSize = int.TryParse(
                    value.AsSpan(typeIdx + 1, value.Length - typeIdx - 1),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out bitSize
                );

                if (!isValidSize || type == eCobType.None)
                    parser.Messages.Add(Message.IllegalTypeName, token, value[typeIdx..]);

                value = value[..typeIdx];
            }

            if (type == eCobType.None)
                type = eCobType.Signed;

            long integerNumber;
            double floatNumber;

            if (hasHexSpecified)
            {
                if (TryParse(value, 16, out integerNumber))
                    return new NumberExpression(token, integerNumber, type, bitSize);
            }
            else if (hasBinSpecified)
            {
                if (TryParse(value, 2, out integerNumber))
                    return new NumberExpression(token, integerNumber, type, bitSize);
            }
            else if (hasDotSpecified
                 &&  double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out floatNumber))
            {
                integerNumber = BitConverter.DoubleToInt64Bits(floatNumber);
                return new NumberExpression(token, integerNumber, type, bitSize);
            }
            else
            {
                if (TryParse(value, 10, out integerNumber))
                    return new NumberExpression(token, integerNumber, type, bitSize);
            }

            parser.Messages.Add(Message.IllegalNumber, token, value);
            return new NumberExpression(token, 0, eCobType.None, 0);
        }

        private static bool TryParse(string value, int fromBase, out long result)
        {
            try
            {
                result = Convert.ToInt64(value, fromBase);
                return true;
            }
            catch
            {
                result = 0;
                return false;
            }
        }
    }
}
