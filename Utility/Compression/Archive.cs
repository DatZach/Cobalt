using Compression.Utility;
using System.IO.Hashing;
using System.Text;

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

        public FileAccess Access { get; private set; }

        public void Dispose()
        {
            Stream.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            await Stream.DisposeAsync();
        }

        public void Commit()
        {
            using var headerStream = new MemoryStream();
            using var header = new BinaryWriter(headerStream);
            using var blobStream = new MemoryStream();
            using var blob = new BinaryWriter(blobStream);

            // Header
            header.Write(HeaderMagic);
            header.Write((uint)0);      // length
            header.Write(Version);
            header.Write((byte)0);      // flags
            header.Write((byte)0);      // RESERVED
            header.Write((uint)0);      // RESERVED

            // Blob
            blob.Write(BlobMagic);
            blob.Write((uint)0);        // length
            blob.Write((uint)0);        // count

            Root.Serialize(header, blob);

            // Flush, Finalize, Write to Disk
            header.Flush();
            blob.Flush();

            header.BaseStream.Position = 4;
            header.Write((uint)(header.BaseStream.Length - 8));

            var count = CountAndDive(Root);
            blob.BaseStream.Position = 4;
            blob.Write((uint)(blob.BaseStream.Length - 8));
            blob.Write(count);

            header.Flush();
            blob.Flush();

            Stream.Position = 0;
            Stream.Write(headerStream.ToArray());
            Stream.Write(blobStream.ToArray());
            Stream.Flush();
            return;

            static int CountAndDive(MapEntry entry)
            {
                int sum = 0;
                if (entry.IsDirectory)
                {
                    foreach (var subEntry in entry.Entries)
                        sum += CountAndDive(subEntry);
                }
                else
                    ++sum;

                return sum;
            }
        }

        public static Archive OpenCreate(string archivePath, string? comment)
        {
            var fileStream = File.OpenWrite(archivePath);

            var archive = new Archive
            {
                Stream = fileStream,
                Access = FileAccess.Write
            };

            archive.Root = MapEntry.NewDirectory(comment ?? "", archive);

            return archive;
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
                Access = FileAccess.Read,
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

            private List<MapEntry>? entries;
            public IReadOnlyList<MapEntry>? Entries => entries;

            public uint OriginalChecksumCRC32 { get; init; }

            private int BlobOffset { get; init; }

            public int UncompressedSize { get; set; }

            private byte[]? uncompressedBuffer;
            public byte[]? UncompressedBuffer
            {
                get => uncompressedBuffer;
                set => uncompressedBuffer = value;
            }

            private byte[]? compressedBuffer;
            public byte[]? CompressedBuffer
            {
                get
                {
                    if (IsDirectory)
                        return null;

                    if (compressedBuffer == null && archive.Access.HasFlag(FileAccess.Read))
                    {
                        using var reader = new BinaryReader(archive.Stream, Encoding.UTF8, true);
                        reader.BaseStream.Position = archive.BlobSectionOffset + BlobOffset;
                        var size = reader.Read7BitEncodedInt();
                        compressedBuffer = reader.ReadBytes(size);
                    }

                    return compressedBuffer;
                }

                set => compressedBuffer = value;
            }

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
                    var compressedBuffer = CompressedBuffer;
                    if (compressedBuffer == null)
                        throw new InvalidDataException("Compressed stream is non-existent");

                    var uncompressedBuffer = Cobpression.Decode(compressedBuffer);

                    // TODO Verify

                    File.WriteAllBytes(path, uncompressedBuffer);
                }
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
                        entries = entries,
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

            public void Serialize(BinaryWriter header, BinaryWriter blob)
            {
                header.Write((byte)(IsDirectory ? 0x01 : 0x00));
                header.Write((uint)CreationTimeUtc.ToCobaltTime());
                header.Write((uint)LastAccessTimeUtc.ToCobaltTime());
                header.Write((uint)LastModifiedTimeUtc.ToCobaltTime());
                header.Write(Name);

                if (IsDirectory)
                {
                    header.Write7BitEncodedInt(Entries.Count);
                    foreach (var entry in Entries)
                        entry.Serialize(header, blob);
                }
                else
                {
                    var srcBuffer = UncompressedBuffer;
                    var dstBuffer = Cobpression.Encode(srcBuffer);

                    var blobOffset = blob.BaseStream.Position;
                    blob.Write7BitEncodedInt(dstBuffer.Length);
                    blob.Write(dstBuffer);

                    header.Write(Crc32.HashToUInt32(srcBuffer));
                    header.Write7BitEncodedInt(srcBuffer.Length);
                    header.Write7BitEncodedInt((int)blobOffset);
                }
            }

            public void Add(string pathOrDirectory) => Add(NewFromFileSystem(pathOrDirectory));

            public void Add(MapEntry entry)
            {
                if (!IsDirectory || entries == null)
                    throw new InvalidOperationException("Cannot add entry to a non-Directory");

                var existingEntry = entries.FirstOrDefault(x => string.Equals(x.Name, entry.Name));
                if (existingEntry != null)
                    throw new InvalidOperationException("Cannot overwrite existing entry of the same name");

                entries.Add(entry);
            }

            public static MapEntry NewDirectory(string name, Archive archive)
            {
                var nowUtc = DateTime.UtcNow;
                return new MapEntry
                {
                    IsDirectory = true,
                    CreationTimeUtc = nowUtc,
                    LastAccessTimeUtc = nowUtc,
                    LastModifiedTimeUtc = nowUtc,
                    Name = name,
                    entries = new List<MapEntry>(),
                    archive = archive
                };
            }

            public static MapEntry NewFile(string name, Archive archive)
            {
                var nowUtc = DateTime.UtcNow;
                return new MapEntry
                {
                    IsDirectory = false,
                    CreationTimeUtc = nowUtc,
                    LastAccessTimeUtc = nowUtc,
                    LastModifiedTimeUtc = nowUtc,
                    Name = name,
                    OriginalChecksumCRC32 = 0,
                    BlobOffset = -1,
                    UncompressedSize = -1,
                    archive = archive
                };
            }

            public static MapEntry NewFromFileSystem(string sourcePathOrDirectory)
            {
                var info = new FileInfo(sourcePathOrDirectory);
                var isDirectory = info.Attributes.HasFlag(FileAttributes.Directory);

                var entry = new MapEntry
                {
                    IsDirectory = isDirectory,
                    CreationTimeUtc = info.CreationTimeUtc,
                    LastAccessTimeUtc = info.LastAccessTimeUtc,
                    LastModifiedTimeUtc = info.LastWriteTimeUtc,
                    Name = info.Name
                };

                if (isDirectory)
                {
                    entry.entries = new List<MapEntry>();

                    var sourceEntries = Directory.EnumerateFileSystemEntries(sourcePathOrDirectory).ToList();
                    foreach (var sourcePath in sourceEntries)
                    {
                        var subEntry = NewFromFileSystem(sourcePath);
                        entry.entries.Add(subEntry);
                    }
                }
                else
                {
                    entry.UncompressedBuffer = File.ReadAllBytes(info.FullName);
                }

                return entry;
            }
        }
    }
}
