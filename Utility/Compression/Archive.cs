using System.IO.Hashing;
using System.Text;
using Compression.Utility;

namespace Compression
{
    internal sealed class Archive : IDisposable, IAsyncDisposable
    {
        private const uint HeaderMagic = 0x56524143;
        private const uint BlobMagic = 0x424F4C42;
        private const ushort Version = 0x0100;

        public MapEntry Root { get; private set; }

        public long BlobSectionOffset { get; private set; }

        public FileStream Stream { get; private set; }

        public void Dispose()
        {
            Stream.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            await Stream.DisposeAsync();
        }

        public static void Pack(string archivePath, IReadOnlyList<string>? sourcePathOrDirectories)
        {
            using var fileStream = File.OpenWrite(archivePath);
            using var headerStream = new MemoryStream();
            using var header = new BinaryWriter(headerStream);
            using var blobStream = new MemoryStream();
            using var blob = new BinaryWriter(blobStream);

            int count = 0;

            // Blob
            blob.Write(BlobMagic);
            blob.Write((uint)0);        // length
            blob.Write((uint)0);        // count

            // Header
            header.Write(HeaderMagic);
            header.Write((uint)0);      // length
            header.Write(Version);
            header.Write((byte)0);      // flags
            header.Write((byte)0);      // RESERVED
            header.Write((uint)0);      // RESERVED

            header.Write((byte)0x01);
            header.Write((uint)DateTime.UtcNow.ToCobaltTime());
            header.Write((uint)DateTime.UtcNow.ToCobaltTime());
            header.Write((uint)DateTime.UtcNow.ToCobaltTime());
            header.Write(""); // TODO Could use to name/describe archive

            header.Write7BitEncodedInt(sourcePathOrDirectories.Count);

            foreach (var topSourcePathOrDirectory in sourcePathOrDirectories)
                SerializeMapEntry(topSourcePathOrDirectory);

            // Flush, Finalize, Write to Disk
            header.Flush();
            blob.Flush();

            header.BaseStream.Position = 4;
            header.Write((uint)(header.BaseStream.Length - 8));

            blob.BaseStream.Position = 4;
            blob.Write((uint)(blob.BaseStream.Length - 8));
            blob.Write(count);

            header.Flush();
            blob.Flush();

            fileStream.Write(headerStream.ToArray());
            fileStream.Write(blobStream.ToArray());
            fileStream.Flush();

            void SerializeMapEntry(string sourcePathOrDirectory)
            {
                var info = new FileInfo(sourcePathOrDirectory);
                var isDirectory = info.Attributes.HasFlag(FileAttributes.Directory);

                header.Write((byte)(isDirectory ? 0x01 : 0x00));
                header.Write((uint)info.CreationTimeUtc.ToCobaltTime());
                header.Write((uint)info.LastAccessTimeUtc.ToCobaltTime());
                header.Write((uint)info.LastWriteTimeUtc.ToCobaltTime());
                header.Write(info.Name);

                if (isDirectory)
                {
                    var entries = Directory.EnumerateFileSystemEntries(sourcePathOrDirectory).ToList();

                    header.Write7BitEncodedInt(entries.Count);
                    foreach (var sourcePath in entries)
                        SerializeMapEntry(sourcePath);
                }
                else
                {
                    var srcBuffer = File.ReadAllBytes(info.FullName);
                    var dstBuffer = Cobpression.Encode(srcBuffer);

                    var blobOffset = blob.BaseStream.Position;
                    blob.Write7BitEncodedInt(dstBuffer.Length);
                    blob.Write(dstBuffer);

                    header.Write(Crc32.HashToUInt32(srcBuffer));
                    header.Write7BitEncodedInt(srcBuffer.Length);
                    header.Write7BitEncodedInt((int)blobOffset);

                    ++count;
                }
            }
        }

        public static Archive OpenRead(string archivePath)
        {
            var fileStream = File.OpenRead(archivePath);
            using var reader = new BinaryReader(fileStream, Encoding.UTF8, true);

            if (reader.ReadUInt32() != HeaderMagic)
                throw new InvalidDataException("Header Magic is not 'CARV'");

            var headerLength = reader.ReadUInt32();
            var blobSectionOffset = headerLength + reader.BaseStream.Position; // TODO Not strictly correct

            if (reader.ReadUInt16() > Version)
                throw new InvalidDataException("Header Version is too new");

            reader.ReadByte(); // flags
            reader.ReadByte(); // RESERVED
            reader.ReadUInt32(); // RESERVED

            var archive = new Archive
            {
                Stream = fileStream,
                BlobSectionOffset = blobSectionOffset
            };

            archive.Root = MapEntry.Deserialize(reader, archive);

            return archive;
        }

        public sealed class MapEntry
        {
            public bool IsDirectory { get; init; }

            public DateTime CreationTimeUtc { get; init; }

            public DateTime LastAccessTimeUtc { get; init; }

            public DateTime LastModifiedTimeUtc { get; init; }

            public string Name { get; init; }

            public IReadOnlyList<MapEntry>? Entries { get; init; }

            public uint OriginalChecksumCRC32 { get; init; }

            public int BlobOffset { get; init; }

            public int UncompressedSize { get; set; }

            private Archive archive { get; init; }

            public MapEntry? Find(string path)
            {
                var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var node = this;
                for (var i = 0; i < parts.Length; ++i)
                {
                    var part = parts[i];
                    node = node.Entries!.FirstOrDefault(x => string.Equals(x.Name, part, StringComparison.OrdinalIgnoreCase));
                    if (node == null || (i < parts.Length - 1 && !node.IsDirectory))
                        return null;
                }

                return node;
            }

            public void Extract(string destinationPathOrDirectory)
            {
                if (IsDirectory)
                {
                    destinationPathOrDirectory = Path.Combine(destinationPathOrDirectory, Name);
                    Directory.CreateDirectory(destinationPathOrDirectory);

                    foreach (var subEntry in Entries!)
                        subEntry.Extract(destinationPathOrDirectory);
                }
                else
                {
                    var path = Path.Combine(destinationPathOrDirectory, Name);
                    var compressedBuffer = GetCompressedBuffer();
                    var uncompressedBuffer = Cobpression.Decode(compressedBuffer);

                    // TODO Verify

                    File.WriteAllBytes(path, uncompressedBuffer);
                }
            }

            public byte[]? GetCompressedBuffer()
            {
                if (IsDirectory)
                    return null;

                using var reader = new BinaryReader(archive.Stream, Encoding.UTF8, true);
                reader.BaseStream.Position = archive.BlobSectionOffset + BlobOffset;
                var size = reader.Read7BitEncodedInt();
                var buffer = reader.ReadBytes(size);

                return buffer;
            }

            public static MapEntry Deserialize(BinaryReader reader, Archive archive)
            {
                var attributes = reader.ReadByte();
                var creationTimeUtc = reader.ReadInt32().FromCobaltTime();
                var lastAccessTimeUtc = reader.ReadInt32().FromCobaltTime();
                var lastModifiedTimeUtc = reader.ReadInt32().FromCobaltTime();
                var name = reader.ReadString();

                if ((attributes & 0x01) != 0)
                {
                    var count = reader.Read7BitEncodedInt();
                    var entries = new List<MapEntry>(count);
                    while (count-- > 0)
                    {
                        var entry = Deserialize(reader, archive);
                        entries.Add(entry);
                    }

                    return new MapEntry
                    {
                        IsDirectory = true,
                        CreationTimeUtc = creationTimeUtc,
                        LastAccessTimeUtc = lastAccessTimeUtc,
                        LastModifiedTimeUtc = lastModifiedTimeUtc,
                        Name = name,
                        Entries = entries,
                        archive = archive
                    };
                }
                else
                {
                    var uncompressedChecksum = reader.ReadUInt32();
                    var uncompressedSize = reader.Read7BitEncodedInt();
                    var blobOffset = reader.Read7BitEncodedInt();

                    return new MapEntry
                    {
                        IsDirectory = false,
                        CreationTimeUtc = creationTimeUtc,
                        LastAccessTimeUtc = lastAccessTimeUtc,
                        LastModifiedTimeUtc = lastModifiedTimeUtc,
                        Name = name,
                        OriginalChecksumCRC32 = uncompressedChecksum,
                        BlobOffset = blobOffset,
                        UncompressedSize = uncompressedSize,
                        archive = archive
                    };
                }
            }
        }
    }
}
