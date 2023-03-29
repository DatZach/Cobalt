using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiskUtil
{
    internal sealed class Cobalt
    {
        private FileSystemConfigBlock? fscb;
        private MasterDescriptorTable? mdt;
        private BTree<Node>? tree;

        private readonly Disk disk;

        public Cobalt(Disk disk)
        {
            this.disk = disk ?? throw new ArgumentNullException(nameof(disk));
        }

        public Node? FindTreeNode(uint nodeId)
        {
            return tree?.Search(nodeId);
        }

        public Node? FindNodeEntry(Node node, string filename, Node.NodeAttributes attributes)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (tree == null) throw new InvalidOperationException();

            if (filename == ".")
                return node;
            if (filename == "..")
                return tree.Search(node.ParentNodeId);
            
            // TODO Support ExtensionNode
            for (var i = 0; i < node.DataSize; ++i)
            {
                var childNodeId = node.Cluster[i];
                var child = tree.Search(childNodeId);
                if (child == null)
                    continue;

                if ((child.Attributes & attributes) == attributes
                &&  string.Equals(child.Name, filename, StringComparison.InvariantCultureIgnoreCase))
                    return child;
            }

            return null;
        }

        public Node? CreateNodeEntry(Node parent, string filename, Node.NodeAttributes attributes)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (tree == null) throw new InvalidOperationException();

            if ((parent.Attributes & Node.NodeAttributes.Directory) != Node.NodeAttributes.Directory)
                throw new InvalidOperationException("Cannot create a child node in file"); // But really we can

            var node = tree.AllocateNode();
            node.ParentNodeId = parent.ParentNodeId;
            node.Attributes = attributes;
            node.ExtFlags = 0; // TODO Support
            node.Name = filename;

            parent.Cluster[parent.DataSize++] = node.Key; // TODO Support ExtensionNode

            return node;
        }

        public void SetNodeContent(Node node, byte[] data)
        {
            if (tree == null) throw new InvalidOperationException();

            var bytesPerCluster = fscb.BytesPerSector * fscb.SectorsPerCluster;
            var buffer = new byte[fscb.BytesPerSector];

            // Existing clusters
            int i = 0, j = 0;
            for (; j < node.DataSize; ++i, j += bytesPerCluster)
            {
                var clusterIndex = node.Cluster[i]; // TODO Support halfClusters!
                Array.Copy(data, j, buffer, 0, Math.Min(bytesPerCluster, data.Length - j)); // TODO Improve disk API
                disk.Write(
                    clusterIndex * fscb.SectorsPerCluster + 1,
                    fscb.SectorsPerCluster,
                    buffer
                );
            }
            
            // Node clusters
            for (; j < data.LongLength; ++i, j += bytesPerCluster)
            {
                var clusterIndex = AllocateCluster(true, false); // TODO Support halfClusters!
                if (clusterIndex == uint.MaxValue)
                    throw new InvalidOperationException("Out of disk space!");

                Array.Copy(data, j, buffer, 0, Math.Min(bytesPerCluster, data.Length - j)); // TODO Improve disk API
                disk.Write(
                    clusterIndex * fscb.SectorsPerCluster + 1,
                    fscb.SectorsPerCluster,
                    buffer
                );

                node.Cluster[i] = clusterIndex;
            }

            node.DataSize = data.LongLength;
        }

        private uint AllocateCluster(bool fullClusters, bool halfClusters)
        {
            var clustersPerBlock = 1 << fscb.LogClustersPerBlock;
            var totalBlocks = fscb.TotalClusters / clustersPerBlock;
            for (int i = 0; i < totalBlocks; ++i)
            {
                // TODO Cache
                var bdtSector = (uint)(i * (clustersPerBlock * fscb.SectorsPerCluster) + 1);
                var bdt = disk.Read<BlockDescriptorTable>(bdtSector);
                var cluster = bdt.AllocateCluster(fullClusters, halfClusters);
                if (cluster != uint.MaxValue)
                {
                    disk.Write(bdtSector, bdt);
                    return cluster;
                }
            }

            return uint.MaxValue;
        }

        public void Mount()
        {
            fscb = disk.Read<FileSystemConfigBlock>(0);
            if (!fscb.IsValid)
                throw new Exception("Invalid FSCB magic, version or checksum (Not a CoFS partition?)");

            mdt = disk.Read<MasterDescriptorTable>(fscb.MdtCluster);

            var rootDescriptor = mdt.EntryOrDefault<RootDescriptor>() ?? throw new Exception("Missing MDT ROOT");

            // TODO It's a bad idea to deserialize the ENTIRE b-tree into memory
            //      it'll get us off the ground for now while we still have small disks
            //      we should keep at least a few clusters buffered, though
            tree = new BTree<Node>(rootDescriptor.Degree);
            tree.Deserialize(disk);

            // TODO Update JRNL to indicate mount state, etc
        }

        public void Dismount()
        {
            // TODO Serialize everything to disk
        }

        public void Format(string volumeName, int sectorsPerCluster, int clustersPerBlock, byte[]? stage1, byte[]? stage2)
        {
            // TODO -1 means auto-figure values

            var clusterSizeBytes = sectorsPerCluster * disk.BytesPerSector;
            var totalClusters = (uint)((disk.TotalSectors - 1) / sectorsPerCluster);
            var totalBlocks = totalClusters / clustersPerBlock;

            var stage2Clusters = stage2 is { Length: > 0 } ? stage2.Length / clusterSizeBytes + 1 : 0;
            var stage2Cluster = uint.MaxValue;
            
            // Block Descriptor Tables
            uint mdtCluster = 0, rootCluster = 0, journalCluster = 0, mdtMirrorCluster = 0;
            for (var i = totalBlocks - 1; i >= 0; --i)
            {
                var bdt = new BlockDescriptorTable((int)i, clustersPerBlock, clusterSizeBytes);
                var bdtSector = (uint)(i * (clustersPerBlock * sectorsPerCluster) + 1);
                if (i == 0)
                {
                    mdtCluster = bdt.AllocateCluster(true, false);

                    if (stage2Clusters > 0)
                    {
                        // TODO AllocateClustersContiguous()
                        stage2Cluster = bdt.AllocateCluster(true, false);
                        for (int j = 1; j < stage2Clusters; ++j)
                            bdt.AllocateCluster(true, false);
                    }

                    rootCluster = bdt.AllocateCluster(true, false);
                }
                else if (i == totalBlocks / 2)
                    journalCluster = bdt.AllocateCluster(true, false);
                else if (i == totalBlocks - 1)
                    mdtMirrorCluster = bdt.AllocateCluster(true, false);

                disk.Write(bdtSector, bdt);
            }
            
            // FSCB
            var fscb = new FileSystemConfigBlock
            {
                Magic = FileSystemConfigBlock.ExpectedMagic,
                Version = FileSystemConfigBlock.ExpectedVersion,
                BytesPerSector = (ushort)disk.BytesPerSector,
                SectorsPerCluster = (byte)sectorsPerCluster,
                LogClustersPerBlock = (byte)FileSystemConfigBlock.Bitlog2(clustersPerBlock),
                TotalClusters = totalClusters,
                MdtCluster = mdtCluster,
                Bootloader = stage1
            };
            fscb.Checksum = FileSystemConfigBlock.CalculateChecksum(fscb);

            disk.Write(0, fscb);

            // Root Directory
            var degree = clusterSizeBytes / 128;
            var overflow = degree * 4 + (degree - 1) * 128 - clusterSizeBytes;
            degree = overflow <= 0 ? degree : degree - overflow / 128;
            degree /= 2; // Don't forget :)
            
            var tree = new BTree<Node>(degree);
            var root = tree.AllocateNode();
            root.Name = volumeName;
            root.Attributes = Node.NodeAttributes.Directory;
            tree.Root.Cluster = rootCluster;

            tree.SerializeTree(disk);

            // Master Descriptor Table
            // TODO Improve the API here
            var mdt = new MasterDescriptorTable(clusterSizeBytes);
            var mdtDescriptor = new MdtDescriptor
            {
                PrimaryOrCopy = 0,
                RootDegree = (byte)tree.Degree,
                BytesPerSector = (ushort)disk.BytesPerSector,
                SectorsPerCluster = fscb.SectorsPerCluster,
                LogClustersPerBlock = fscb.LogClustersPerBlock,
                TotalClusters = fscb.TotalClusters,
                MdtCluster = fscb.MdtCluster,
                MirrorCluster = mdtMirrorCluster
            };
            mdt.Entries.Add(mdtDescriptor);
            mdt.Entries.Add(new JournalDescriptor
            {
                Flags = JournalDescriptor.JournalFlags.MountCountCheck
                      | JournalDescriptor.JournalFlags.MountTimestampCheck
                      | JournalDescriptor.JournalFlags.Journaling,
                JournalCluster = journalCluster
            });
            mdt.Entries.Add(new RootDescriptor
            {
                RootCluster = rootCluster,
                LastNodeId = tree.LastNodeId,
                Degree = (byte)tree.Degree
            });
            mdt.Entries.Add(new BootDescriptor
            {
                Cluster = stage2Cluster,
                Size = (uint)stage2Clusters
            });

            disk.Write((uint)(mdtCluster * sectorsPerCluster + 1), mdt);
            mdtDescriptor.PrimaryOrCopy = 1;
            disk.Write((uint)(mdtMirrorCluster * sectorsPerCluster + 1), mdt);

            // Journal
            var journalSuper = new JournalSuper
            {
                LastMountTimestamp = DateTime.UtcNow.ToCobaltTime(),
                MountsSinceLastCheck = 0,
                MountState = 0
            };

            disk.Write((uint)(journalCluster * sectorsPerCluster + 1), journalSuper);

            // Boot
            if (stage2 != null)
            {
                var stage2Buffer = new byte[stage2Clusters * sectorsPerCluster * disk.BytesPerSector];
                Array.Copy(stage2, stage2Buffer, stage2.Length);
                disk.Write((uint)(stage2Cluster * sectorsPerCluster + 1), stage2Clusters * sectorsPerCluster, stage2Buffer);
            }
        }
    }

    internal sealed record FileSystemConfigBlock : IDiskSerializable
    {
        public const uint ExpectedMagic = 0x53466F43;

        public const byte ExpectedVersion = 1;

        public uint Magic { get; set; }

        public byte Version { get; set; }

        public ushort BytesPerSector { get; set; }

        public byte SectorsPerCluster { get; set; }

        public byte LogClustersPerBlock { get; set; }

        public uint TotalClusters { get; set; }

        public uint MdtCluster { get; set; }

        public uint Checksum { get; set; }

        public byte[]? Bootloader { get; set; }

        public bool IsValid => Magic == ExpectedMagic
                            && Version >= ExpectedVersion
                            && Checksum == CalculateChecksum(this);

        public int SizeOnDiskBytes => 512;

        public FileSystemConfigBlock()
        {
            // NOTE Empty ctor required for serialization
        }
        
        public void Serialize(BinaryWriter writer)
        {
            if (Bootloader != null)
                writer.Write(Bootloader);

            writer.BaseStream.Position = 8;
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(BytesPerSector);
            writer.Write(SectorsPerCluster);
            writer.Write(LogClustersPerBlock);
            writer.Write(TotalClusters);
            writer.Write(MdtCluster);
            writer.Write(Checksum);
        }

        public void Deserialize(BinaryReader reader)
        {
            Bootloader = new byte[SizeOnDiskBytes];
            reader.Read(Bootloader, 0, SizeOnDiskBytes);

            reader.BaseStream.Position = 8;
            Magic = reader.ReadUInt32();
            Version = reader.ReadByte();
            BytesPerSector = reader.ReadUInt16();
            SectorsPerCluster = reader.ReadByte();
            LogClustersPerBlock = reader.ReadByte();
            TotalClusters = reader.ReadUInt32();
            MdtCluster = reader.ReadUInt32();
            Checksum = reader.ReadUInt32();
        }

        public static uint CalculateChecksum(FileSystemConfigBlock fscb)
        {
            if (fscb == null) throw new ArgumentNullException(nameof(fscb));

            return (
                + (uint)fscb.Version
                + (uint)fscb.BytesPerSector
                + (uint)fscb.SectorsPerCluster
                + (uint)fscb.LogClustersPerBlock
                + (uint)fscb.TotalClusters
                + (uint)fscb.MdtCluster
            ) ^ fscb.Magic;
        }

        public static int Bitlog2(int value)
        {
            int result = 0;
            while ((value >>= 1) != 0)
                ++result;
            return result;
        }
    }

    internal sealed record BlockDescriptorTable : IDiskSerializable
    {
        public const uint ExpectedMagic = 0x4B4F4C42;

        public uint Magic { get; private set; }

        public StatusFlags Status { get; set; }

        public int FirstFreeFullCluster { get; set; }

        public int FirstFreeHalfCluster { get; set; }

        public byte[] Bitmap { get; private set; }

        public int SizeOnDiskBytes { get; private set; }

        private readonly uint clusterOffset;

        public BlockDescriptorTable()
        {
            // NOTE Required for interface
        }

        public BlockDescriptorTable(int blockIndex, int clustersPerBlock, int clusterSize)
        {
            var bitmapSize = (clustersPerBlock + clustersPerBlock / 2) / 8;
            var reservedBits = (bitmapSize + 512 + (clusterSize - 1)) / clusterSize;

            clusterOffset = (uint)blockIndex * (uint)clustersPerBlock;
            SizeOnDiskBytes = (clustersPerBlock + clustersPerBlock / 2) / 8 + 512;

            Magic = ExpectedMagic;
            Status = 0;
            FirstFreeFullCluster = reservedBits;
            FirstFreeHalfCluster = clustersPerBlock;
            Bitmap = new byte[bitmapSize];

            for (int i = 0; i < reservedBits; ++i)
            {
                var bo = i / 8;
                var bi = i % 8;
                Bitmap[bo] |= (byte)(1 << bi);
            }
        }

        public uint AllocateCluster(bool fullClusters, bool halfClusters)
        {
            if ((Status & StatusFlags.Full) == StatusFlags.Full)
                return uint.MaxValue;

            var bitmapSize = Bitmap.Length * 8;
            
            // Prefer half clusters
            if (halfClusters)
            {
                throw new NotImplementedException(); // lol
            }

            if (fullClusters)
            {
                var offset = 0;
                var count = bitmapSize - bitmapSize / 3; // bitmapSize / 1.5
                for (int i = 0; i < bitmapSize; ++i)
                {
                    var idx = (i + FirstFreeFullCluster) % count + offset;
                    var bo = idx / 8;
                    var bi = idx % 8;
                    var mask = 1 << bi;
                    if ((Bitmap[bo] & mask) == 0)
                    {
                        Bitmap[bo] |= (byte)mask;
                        FirstFreeFullCluster = (idx + 1) % count;
                        return (uint)idx + clusterOffset;
                    }
                }
            }

            // TODO Should probably actually split this flag to FullFull and HalfFull... you know what I mean
            if (fullClusters && halfClusters)
                Status |= StatusFlags.Full;

            return uint.MaxValue;
        }
        
        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Magic);
            writer.Write((byte)Status);
            writer.Write(FirstFreeFullCluster);
            writer.Write(FirstFreeHalfCluster);
            writer.BaseStream.Position = 512; // TODO Don't hardcode
            writer.Write(Bitmap);
        }

        public void Deserialize(BinaryReader reader)
        {
            var clustersPerBlock = 0;
            var bitmapSize = (clustersPerBlock + clustersPerBlock / 2) / 8;
            SizeOnDiskBytes = (clustersPerBlock + clustersPerBlock / 2) / 8 + 512;

            Magic = reader.ReadUInt32();
            Status = (StatusFlags)reader.ReadByte();
            FirstFreeFullCluster = reader.ReadInt32();
            FirstFreeHalfCluster = reader.ReadInt32();
            reader.BaseStream.Position = 512;
            Bitmap = new byte[bitmapSize];
            reader.Read(Bitmap, 0, bitmapSize);
        }

        [Flags]
        public enum StatusFlags : byte
        {
            None = 0x00,
            Full = 0x01
        }
    }

    internal sealed class MasterDescriptorTable : IDiskSerializable
    {
        private const int BytesPerEntry = 32;

        public List<MasterDescriptorTableEntry> Entries { get; }

        public int SizeOnDiskBytes => 4096; // TODO AAAAA

        private readonly static Dictionary<uint, Type> types;
        static MasterDescriptorTable()
        {
            types = new Dictionary<uint, Type>();
            var asm = typeof(MasterDescriptorTable).Assembly;
            foreach (var type in asm.GetTypes()
                         .Where(x => x.IsClass && !x.IsAbstract && x.IsSubclassOf(typeof(MasterDescriptorTableEntry))))
            {
                var inst = (MasterDescriptorTableEntry)Activator.CreateInstance(type)!;
                types.Add(inst.Type, type);
            }
        }

        public MasterDescriptorTable()
        {

        }

        public MasterDescriptorTable(int clusterSize)
        {
            Entries = new List<MasterDescriptorTableEntry>();
            //SizeOnDiskBytes = clusterSize;
        }

        public T? EntryOrDefault<T>()
            where T : MasterDescriptorTableEntry
        {
            // TODO Perhaps we should also cache these in a dictionary for O(1)?
            //      how often is this going to be called?
            foreach (var entry in Entries)
            {
                if (entry is T tableEntry)
                    return tableEntry;
            }

            return default;
        }

        public void Serialize(BinaryWriter writer)
        {
            for (var i = 0; i < Entries.Count; i++)
            {
                var entry = Entries[i];

                writer.BaseStream.Position = i * 32;
                writer.Write(entry.Type);
                entry.Serialize(writer);
            }

            // TODO Support MDXT
        }

        public void Deserialize(BinaryReader reader)
        {
            var bytesPerCluster = 4096; // TODO No

            Entries.Clear();
            for (int i = 0; i < bytesPerCluster / BytesPerEntry; ++i)
            {
                reader.BaseStream.Position = i * 32;
                var key = reader.ReadUInt32();

                // NOTE Not knowing an MDT entry is NOT an error, this is a modular filesystem after all
                if (!types.TryGetValue(key, out var type))
                    continue;

                var entry = (MasterDescriptorTableEntry)Activator.CreateInstance(type)!;
                entry.Deserialize(reader);

                Entries.Add(entry);
            }

            // TODO Support MDXT
        }
    }

    internal abstract class MasterDescriptorTableEntry
    {
        public abstract uint Type { get; }

        public abstract void Serialize(BinaryWriter writer);

        public abstract void Deserialize(BinaryReader reader);
    }

    internal sealed class MdtDescriptor : MasterDescriptorTableEntry
    {
        public override uint Type => 0x7F54444D;

        public byte PrimaryOrCopy { get; set; }

        public byte RootDegree { get; set; }

        public ushort BytesPerSector { get; set; }

        public byte SectorsPerCluster { get; set; }

        public byte LogClustersPerBlock { get; set; }

        public uint TotalClusters { get; set; }

        public uint MdtCluster { get; set; }

        public uint MirrorCluster { get; set; }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(PrimaryOrCopy);
            writer.Write((ushort)0); // reserved
            writer.Write(RootDegree);
            writer.Write(BytesPerSector);
            writer.Write(SectorsPerCluster);
            writer.Write(LogClustersPerBlock);
            writer.Write(TotalClusters);
            writer.Write(MdtCluster);
            writer.Write(MirrorCluster);
        }

        public override void Deserialize(BinaryReader reader)
        {
            PrimaryOrCopy = reader.ReadByte();
            RootDegree = reader.ReadByte();
            BytesPerSector = reader.ReadUInt16();
            SectorsPerCluster = reader.ReadByte();
            LogClustersPerBlock = reader.ReadByte();
            TotalClusters = reader.ReadUInt32();
            MdtCluster = reader.ReadUInt32();
            MirrorCluster = reader.ReadUInt32();
        }
    }

    internal sealed class JournalDescriptor : MasterDescriptorTableEntry
    {
        public override uint Type => 0x4C4E524A;

        public JournalFlags Flags { get; set; }

        public uint JournalCluster { get; set; }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write((byte)Flags);
            writer.Write((byte)0);      // reserved
            writer.Write((ushort)0);    // reserved
            writer.Write(JournalCluster);
        }

        public override void Deserialize(BinaryReader reader)
        {
            Flags = (JournalFlags)reader.ReadByte();
            reader.ReadByte();          // reserved
            reader.ReadUInt16();        // reserved
            JournalCluster = reader.ReadUInt32();
        }

        [Flags]
        public enum JournalFlags : byte
        {
            None                = 0x00,
            MountCountCheck     = 0x01,
            MountTimestampCheck = 0x02,
            Journaling          = 0x04
        }
    }

    internal sealed class RootDescriptor : MasterDescriptorTableEntry
    {
        public override uint Type => 0x544F4F52;

        public uint RootCluster { get; set; }

        public uint LastNodeId { get; set; }

        public byte Degree { get; set; }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(RootCluster);
            writer.Write(LastNodeId);
            writer.Write(Degree);
        }

        public override void Deserialize(BinaryReader reader)
        {
            RootCluster = reader.ReadUInt32();
            LastNodeId = reader.ReadUInt32();
            Degree = reader.ReadByte();
        }
    }

    internal sealed class BootDescriptor : MasterDescriptorTableEntry
    {
        public override uint Type => 0x544F4F42;

        public uint Cluster { get; set; }

        public uint Size { get; set; }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(Cluster);
            writer.Write(Size);
        }

        public override void Deserialize(BinaryReader reader)
        {
            Cluster = reader.ReadUInt32();
            Size = reader.ReadUInt32();
        }
    }
    
    internal sealed class Node : IBTreeEntry
    {
        public uint Key { get; set; } // NodeId

        public uint ParentNodeId { get; set; }

        public uint ExtensionNodeId { get; set; }

        public NodeAttributes Attributes { get; set; }

        public byte ExtFlags { get; set; }

        //public byte NameSize { get; set; }

        public byte Reserved0 { get; set; }

        public long DataSize { get; set; }

        public int CreationTime { get; set; }

        public int LastAccessTime { get; set; }

        public int LastModificationTime { get; set; }

        public uint[] Cluster { get; set; } // Or ChildNodeIds if Directory

        public byte[] NodeExtensions { get; set; }

        public string Name { get; set; }

        public Node()
        {
            Key = uint.MaxValue;
            ParentNodeId = uint.MaxValue;
            ExtensionNodeId = uint.MaxValue;
            CreationTime = DateTime.UtcNow.ToCobaltTime();
            LastAccessTime = CreationTime;
            LastModificationTime = CreationTime;
            Cluster = new uint[16];
        }

        public int CompareTo(IBTreeEntry? other)
        {
            if (ReferenceEquals(this, other)) return 0;
            if (ReferenceEquals(null, other)) return 1;
            var result = Key.CompareTo(other.Key);
            return result;
        }

        public override string ToString()
        {
            return $"[{Key}] \'{Name}\'";
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Key);
            writer.Write(ParentNodeId);
            writer.Write(ExtensionNodeId);
            writer.Write((byte)Attributes);
            writer.Write(ExtFlags);
            writer.Write((byte)Name.Length);
            writer.Write(Reserved0);
            writer.Write(DataSize);
            writer.Write(CreationTime);
            writer.Write(LastAccessTime);
            writer.Write(LastModificationTime);
            for (int i = 0; i < Cluster.Length; ++i)
                writer.Write(Cluster[i]);
            for (int i = 0; i < Name.Length && i < 28; ++i)
                writer.Write(Name[i]);
            writer.BaseStream.Position += Math.Max(0, 28 - Name.Length);
        }

        public void Deserialize(BinaryReader reader)
        {
            Key = reader.ReadUInt32();
            ParentNodeId = reader.ReadUInt32();
            ExtensionNodeId = reader.ReadUInt32();
            Attributes = (NodeAttributes)reader.ReadByte();
            ExtFlags = reader.ReadByte();
            var NameSize = reader.ReadByte();
            Reserved0 = reader.ReadByte();
            DataSize = reader.ReadUInt64();
            CreationTime = reader.ReadInt32();
            LastAccessTime = reader.ReadInt32();
            LastModificationTime = reader.ReadInt32();
            for (int i = 0; i < 16; ++i)
                Cluster[i] = reader.ReadUInt32();
            var len = Math.Min(28, (int)NameSize);
            var data = new byte[len];
            reader.Read(data, 0, len);
            Name = Encoding.UTF8.GetString(data);
            reader.BaseStream.Position += Math.Max(0, 28 - NameSize);
        }

        public int SizeOnDiskBytes { get; }

        [Flags]
        public enum NodeAttributes : byte
        {
            None            = 0x00,
            Directory       = 0x01,
            ReadOnly        = 0x02,
            Hidden          = 0x04,
            ExtendedNode    = 0x08,
            Deleted         = 0x80
        }
    }

    internal sealed class JournalSuper : IDiskSerializable
    {
        public ushort MountsSinceLastCheck { get; set; }

        public int LastMountTimestamp { get; set; }

        public byte MountState { get; set; }

        public int SizeOnDiskBytes => 512;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(MountsSinceLastCheck);
            writer.Write(LastMountTimestamp);
            writer.Write(MountState);
        }

        public void Deserialize(BinaryReader reader)
        {
            throw new NotImplementedException();
        }
    }
}
