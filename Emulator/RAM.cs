using System.Text;

namespace Emulator
{
    public sealed class RAM
    {
        public int Size => data.Length;

        private readonly byte[] data;

        public RAM(int size)
        {
            data = new byte[size];
        }

        public byte ReadByte(ushort segment, ushort offset) => data[ToCanonicalAddress(segment, offset)];

        public void WriteByte(ushort segment, ushort offset, byte value) =>
            data[ToCanonicalAddress(segment, offset)] = value;

        public ushort ReadWord(ushort segment, ushort offset) =>
            (ushort)((data[ToCanonicalAddress(segment, (ushort)(offset + 0))] << 8)
                     | data[ToCanonicalAddress(segment, (ushort)(offset + 1))]);

        public void WriteWord(ushort segment, ushort offset, ushort value)
        {
            data[ToCanonicalAddress(segment, (ushort)(offset + 1))] = (byte)(value & 0xFF);
            data[ToCanonicalAddress(segment, (ushort)(offset + 0))] = (byte)((value >> 8) & 0xFF);
        }

        private static long ToCanonicalAddress(ushort segment, ushort offset)
        {
            return ((long)segment << 16) | offset;
        }

        public RAM CaptureState()
        {
            var capture = new RAM(Size);
            Array.Copy(data, capture.data, Size);

            return capture;
        }

        public string ToString(int offset, int length)
        {
            var sb = new StringBuilder();

            sb.Append("     ");
            for (int j = 0; j < 16; ++j)
                sb.Append($"{j:X2} ");
            sb.AppendLine();

            var rows = length / 16;
            for (int i = 0; i < rows; ++i)
            {
                sb.Append($"{offset + (i * 16):X4} ");
                for (int j = 0; j < 16; ++j)
                {
                    var idx = i * 16 + j;
                    if (idx >= length)
                        break;

                    sb.Append($"{ReadByte(0, (ushort)(offset + idx)):X2} ");
                }

                //sb.Append("\t");

                //for (int j = 0; j < 16; ++j)
                //{
                //    var idx = i * 16 + j;
                //    if (idx >= length)
                //        break;

                //    char ch = (char)ReadByte(0, (ushort)(offset + idx));
                //    if (!char.IsAscii(ch) || char.IsControl(ch)) ch = '.';
                //    sb.Append($"{ch}");
                //}

                sb.AppendLine();
            }

            return sb.ToString();
        }

        public override string ToString()
        {
            return ToString(0, Size);
        }
    }
}