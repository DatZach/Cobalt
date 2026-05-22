using System;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Compression.Utility;

namespace Compression
{
    internal class Archive
    {
        private const uint HeaderMagic = 0x56524143;
        private const uint BlobMagic = 0x424F4C42;
        private const ushort Version = 0x0100;

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

        public static void Unpack(string archivePath, string destinationDirectory)
        {
            using var fileStream = File.OpenRead(archivePath);
            using var reader = new BinaryReader(fileStream);

            if (reader.ReadUInt32() != HeaderMagic)
                throw new InvalidDataException("Header Magic is not 'CARV'");

            var headerLength = reader.ReadUInt32();
            var blobSectionOffset = headerLength + reader.BaseStream.Position; // TODO Not strictly correct

            if (reader.ReadUInt16() > Version)
                throw new InvalidDataException("Header Version is too new");

            reader.ReadByte(); // flags
            reader.ReadByte(); // RESERVED
            reader.ReadUInt32(); // RESERVED

            var rootDirectory = DeserializeMapEntry();

            ExtractMapEntry(rootDirectory, destinationDirectory);

            MapEntry DeserializeMapEntry()
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
                        var entry = DeserializeMapEntry();
                        entries.Add(entry);
                    }

                    return new MapEntry
                    {
                        IsDirectory = true,
                        CreationTimeUtc = creationTimeUtc,
                        LastAccessTimeUtc = lastAccessTimeUtc,
                        LastModifiedTimeUtc = lastModifiedTimeUtc,
                        Name = name,
                        Entries = entries
                    };
                }
                else
                {
                    var uncompressedChecksum = reader.ReadUInt32();
                    var uncompressedSize = reader.Read7BitEncodedInt();
                    var blobOffset = reader.Read7BitEncodedInt();

                    var oldPosition = reader.BaseStream.Position;
                    reader.BaseStream.Position = blobSectionOffset + blobOffset;

                    var compressedSize = reader.Read7BitEncodedInt();
                    var compressedBuffer = reader.ReadBytes(compressedSize);

                    reader.BaseStream.Position = oldPosition;

                    return new MapEntry
                    {
                        IsDirectory = false,
                        CreationTimeUtc = creationTimeUtc,
                        LastAccessTimeUtc = lastAccessTimeUtc,
                        LastModifiedTimeUtc = lastModifiedTimeUtc,
                        Name = name,
                        OriginalChecksumCRC32 = uncompressedChecksum,
                        CompressedBuffer = compressedBuffer,
                        UncompressedSize = uncompressedSize
                    };
                }
            }

            void ExtractMapEntry(MapEntry entry, string destinationDirectory)
            {
                if (entry.IsDirectory)
                {
                    destinationDirectory = Path.Combine(destinationDirectory, entry.Name);
                    Directory.CreateDirectory(destinationDirectory);

                    foreach (var subEntry in entry.Entries)
                        ExtractMapEntry(subEntry, destinationDirectory);
                }
                else
                {
                    var path = Path.Combine(destinationDirectory, entry.Name);
                    var uncompressedBuffer = Cobpression.Decode(entry.CompressedBuffer);

                    // TODO Verify

                    File.WriteAllBytes(path, uncompressedBuffer);
                }
            }
        }

        private sealed class MapEntry
        {
            public bool IsDirectory { get; set; }

            public DateTime CreationTimeUtc { get; set; }

            public DateTime LastAccessTimeUtc { get; set; }

            public DateTime LastModifiedTimeUtc { get; set; }

            public string Name { get; set; }

            public IReadOnlyList<MapEntry> Entries { get; set; }

            public uint OriginalChecksumCRC32 { get; set; }

            public byte[] CompressedBuffer { get; set; }

            public int UncompressedSize { get; set; }

            public int CompressedSize => CompressedBuffer.Length;
        }
    }
}
