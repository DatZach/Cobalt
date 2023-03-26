using System;
using System.IO;

namespace DiskUtil
{
    internal sealed class Disk : IDisposable
    {
        public long TotalSectors { get; init; }

        public int BytesPerSector { get; init; }

        private FileStream Stream { get; init; }

        private Disk()
        {
            // NOTE Private ctor to enforce factory pattern
        }

        public void Write(uint sector, int count, byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length < BytesPerSector) throw new InvalidDataException("data must be sector size");

            Stream.Position = sector * BytesPerSector;
            Stream.Write(data, 0, count * BytesPerSector);
        }

        public byte[] Read(uint sector, int count)
        {
            var size = count * BytesPerSector;
            var buffer = new byte[size];

            Stream.Position = sector * BytesPerSector;
            if (Stream.Read(buffer, 0, size) != size)
                throw new InvalidDataException();

            return buffer;
        }

        public void Write(uint sector, IDiskSerializable data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            var count = (data.SizeOnDiskBytes + (BytesPerSector - 1)) / BytesPerSector;
            var buffer = new byte[count * BytesPerSector];
            using var ms = new MemoryStream(buffer);
            using var bw = new BinaryWriter(ms);

            data.Serialize(bw);
            bw.Flush();

            Write(sector, count, buffer);
        }

        public IDiskSerializable Read<T>(uint sector)
            where T : IDiskSerializable, new()
        {
            var value = new T();
            var buffer = Read(sector, value.SizeOnDiskBytes / BytesPerSector);
            using var ms = new MemoryStream(buffer);
            using var br = new BinaryReader(ms);

            value.Deserialize(br);

            return value;
        }

        public void Dispose()
        {
            Stream.Dispose();
        }

        public static Disk FromFile(string path, int sectorSize)
        {
            var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            return new Disk
            {
                Stream = stream,
                BytesPerSector = sectorSize,
                TotalSectors = stream.Length / sectorSize
            };
        }
    }

    internal interface IDiskSerializable
    {
        int SizeOnDiskBytes { get; }

        void Serialize(BinaryWriter writer);

        void Deserialize(BinaryReader reader);
    }
}
