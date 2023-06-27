namespace Emulator
{
    internal sealed class RAM
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
    }
}