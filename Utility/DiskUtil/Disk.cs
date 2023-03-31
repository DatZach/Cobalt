using System;
using System.IO;

namespace DiskUtil
{
    internal sealed class Disk : IDisposable
    {
        public long TotalSectors { get; init; }

        public int BytesPerSector { get; init; }

        public int SectorsPerCluster { get; set; }

        private FileStream Stream { get; init; }

        private long OffsetSector { get; init; }

        private Disk()
        {
            // NOTE Private ctor to enforce factory pattern
        }

        public void WriteCluster(long cluster, byte[] data)
        {
            if (SectorsPerCluster == 0) throw new InvalidOperationException("SectorsPerCluster == 0");

            var sector = cluster * SectorsPerCluster + 1;
            WriteSectors(sector, SectorsPerCluster, data);
        }

        public byte[] ReadCluster(long cluster)
        {
            if (SectorsPerCluster == 0) throw new InvalidOperationException("SectorsPerCluster == 0");

            var sector = cluster * SectorsPerCluster + 1;
            return ReadSectors(sector, SectorsPerCluster);
        }

        public void WriteCluster(long cluster, IDiskSerializable data)
        {
            var sector = cluster * SectorsPerCluster + 1;
            WriteSectors(sector, data); // TODO Not technically correct because it might under/overflow a cluster
        }

        public T ReadCluster<T>(long cluster)
            where T : IDiskSerializable, new()
        {
            var value = new T();
            var sector = cluster * SectorsPerCluster + 1;
            var buffer = ReadSectors(sector, SectorsPerCluster);
            using var ms = new MemoryStream(buffer);
            using var br = new BinaryReader(ms);

            value.Deserialize(br);

            return value;
        }

        public void WriteSectors(long sector, int count, byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (count * BytesPerSector != data.Length) throw new ArgumentException("data must be sector size * count");
            if (sector + count >= OffsetSector + TotalSectors) throw new ArgumentException("Would overwrite partition bounds");

            Stream.Position = (OffsetSector + sector) * BytesPerSector;
            Stream.Write(data, 0, count * BytesPerSector);
        }

        public byte[] ReadSectors(long sector, int count)
        {
            var size = count * BytesPerSector;
            var buffer = new byte[size];

            Stream.Position = (OffsetSector + sector) * BytesPerSector;
            if (Stream.Read(buffer, 0, size) != size)
                throw new InvalidDataException();

            return buffer;
        }

        public void WriteSectors(long sector, IDiskSerializable data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            var count = (data.SizeOnDiskBytes + (BytesPerSector - 1)) / BytesPerSector;
            var buffer = new byte[count * BytesPerSector];
            using var ms = new MemoryStream(buffer);
            using var bw = new BinaryWriter(ms);

            data.Serialize(bw);
            bw.Flush();

            WriteSectors(sector, count, buffer);
        }

        public T ReadSectors<T>(long sector)
            where T : IDiskSerializable, new()
        {
            var value = new T();
            var buffer = ReadSectors(sector, (value.SizeOnDiskBytes + (BytesPerSector - 1)) / BytesPerSector);
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
