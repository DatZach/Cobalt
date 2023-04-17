using Compiler.Lexer;
using Compiler.Ast.Expressions;
using System.Globalization;
using Compiler.CodeGeneration;

namespace Compiler.Ast.Parselets
{
    internal sealed class NumberParselet : IPrefixExpressionParselet
    {
        public Expression Parse(Parser parser, Token token)
        {
            var value = token.Value;
            var hasHexSpecified = false;
            var hasBinSpecified = false;
            var hasDotSpecified = false;
            var type = eCobType.Unsigned;
            var bitSize = 32; // TODO Parse suffix

            int i = 0;
            if (value[0] == '-' || value[0] == '+')
            {
                type = eCobType.Signed;
                ++i;
            }

            if (value.Length > 2 && value[i] == '0' && value[i + 1] == 'x')
            {
                value = token.Value[2..];
                hasHexSpecified = true;
            }
            else if (value.Length > 2 && value[i] == '0' && value[i + 1] == 'b')
            {
                value = token.Value[2..];
                hasBinSpecified = true;
            }
            else if (value.IndexOf('.') != -1)
            {
                bitSize = 64;
                hasDotSpecified = true;
            }

            if (value.IndexOf('_') != -1)
                value = value.Replace("_", "");

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
                 &&  double.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                                     CultureInfo.InvariantCulture, out floatNumber))
            {
                integerNumber = BitConverter.DoubleToInt64Bits(floatNumber);
                return new NumberExpression(token, integerNumber, eCobType.Float, bitSize);
            }
            else
            {
                if (TryParse(value, 10, out integerNumber))
                    return new NumberExpression(token, integerNumber, type, bitSize);
            }

            throw new Exception($"Invalid number '{token.Value}'");
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
