using Compiler.Lexer;
using Compiler.Ast.Expressions;
using System.Globalization;
using Compiler.CodeGeneration.Artifacts;

namespace Compiler.Ast.Parselets
{
    internal sealed class NumberLiteralParselet : IPrefixExpressionParselet
    {
        private static readonly char[] TypeSpecifiers = { 'u', 's', 'r' };

        public Expression Parse(Parser parser, Token token)
        {
            var value = token.Value;
            var hasHexSpecified = false;
            var hasBinSpecified = false;
            var hasDotSpecified = false;
            var type = eCobType.None;
            var bitSize = -1;

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
                type = eCobType.Real;
                hasDotSpecified = true;
            }

            if (value.IndexOf('_') != -1)
                value = value.Replace("_", "");

            int typeIdx = value.IndexOfAny(TypeSpecifiers);
            if (typeIdx != -1)
            {
                var suffixType = value[typeIdx] switch
                {
                    'u' => eCobType.Unsigned,
                    's' => eCobType.Signed,
                    'r' => eCobType.Real,
                    _ => eCobType.None
                };

                var suffixLength = value.Length - typeIdx - 1;
                if (suffixLength > 0)
                {
                    var isValidSize = int.TryParse(
                        value.AsSpan(typeIdx + 1, value.Length - typeIdx - 1),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out bitSize
                    );

                    if (!isValidSize || suffixType == eCobType.None)
                        parser.Messages.Add(Message.IllegalTypeName, token, value[typeIdx..]);
                }
                else
                    bitSize = CobType.SizePlatformSpecified;

                if (suffixType == eCobType.Unsigned && type == eCobType.Signed)
                    parser.Messages.Add(Message.IllegalNumber, token, token.Value);

                value = value[..typeIdx];
                type = suffixType;
            }

            if (type == eCobType.None)
                type = eCobType.Signed;

            long integerNumber;

            if (hasHexSpecified)
            {
                if (TryParse(value, 16, type, bitSize, out integerNumber))
                    return new NumberLiteralExpression(token, integerNumber, type, bitSize);
            }
            else if (hasBinSpecified)
            {
                if (TryParse(value, 2, type, bitSize, out integerNumber))
                    return new NumberLiteralExpression(token, integerNumber, type, bitSize);
            }
            else if (hasDotSpecified)
            {
                if (TryParse(value, 10, type, bitSize, out integerNumber))
                    return new NumberLiteralExpression(token, integerNumber, type, bitSize);
            }
            else
            {
                if (TryParse(value, 10, type, bitSize, out integerNumber))
                    return new NumberLiteralExpression(token, integerNumber, type, bitSize);
            }

            parser.Messages.Add(Message.IllegalNumber, token, token.Value);
            return new NumberLiteralExpression(token, 0, eCobType.None, 0);
        }

        private static bool TryParse(string value, int fromBase, eCobType t, int w, out long result)
        {
            try
            {
                if (t == eCobType.Signed)
                {
                    if (w == -1)
                        result = Convert.ToInt64(value, fromBase);
                    else if (w <= 8)
                        result = Convert.ToSByte(value, fromBase);
                    else if (w <= 16)
                        result = Convert.ToInt16(value, fromBase);
                    else if (w <= 32)
                        result = Convert.ToInt32(value, fromBase);
                    else if (w <= 64)
                        result = Convert.ToInt64(value, fromBase);
                    else
                        throw new OverflowException();
                }
                else if (t == eCobType.Unsigned)
                {
                    if (w == -1)
                        result = (long)Convert.ToUInt64(value, fromBase);
                    else if (w <= 8)
                        result = Convert.ToByte(value, fromBase);
                    else if (w <= 16)
                        result = Convert.ToUInt16(value, fromBase);
                    else if (w <= 32)
                        result = Convert.ToUInt32(value, fromBase);
                    else if (w <= 64)
                        result = (long)Convert.ToUInt64(value, fromBase);
                    else
                        throw new OverflowException();
                }
                else if (t == eCobType.Real)
                {
                    if (w == -1)
                        result = BitConverter.DoubleToInt64Bits(double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture));
                    else if (w == 32)
                        result = BitConverter.SingleToInt32Bits(float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture));
                    else if (w == 64)
                        result = BitConverter.DoubleToInt64Bits(double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture));
                    else
                        throw new OverflowException();
                }
                else
                    throw new ArgumentException();

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
