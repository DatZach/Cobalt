namespace Emulator
{
    internal sealed class AtaDevice : DeviceBase<AtaDevice.ConfigDefinition>
    {
        private const ushort RegAltStatus_DevControl = 0;
        private const ushort RegData = 1;
        private const ushort RegError_Features = 2;
        private const ushort RegSectorCount = 3;
        private const ushort RegLBA0 = 4;
        private const ushort RegLBA1 = 5;
        private const ushort RegLBA2 = 6;
        private const ushort RegLBA3 = 7;
        private const ushort RegStatus_Command = 8;

        private const byte DCR_SRST = 0b00000100;

        private const byte SR_ERR  = 0b00000001;
        private const byte SR_IDX  = 0b00000010;
        private const byte SR_CORR = 0b00000100;
        private const byte SR_DRQ  = 0b00001000;
        private const byte SR_DSC  = 0b00010000;
        private const byte SR_DWF  = 0b00100000;
        private const byte SR_DRDY = 0b01000000;
        private const byte SR_BSY  = 0b10000000;

        // NOTE Read/Write to this does not affect the interrupt flag
        private int StatusFlags
        {
            get => registers.ReadByte(0, RegStatus_Command * 2 + 1);
            set
            {
                registers.WriteByte(0, RegStatus_Command * 2 + 1, (byte)value);
                registers.WriteByte(0, RegAltStatus_DevControl * 2 + 1, (byte)value);
            }
        }

        public override string Name => "ATA Storage";

        public override short DevAddrLo => 0x0B;
        public override short DevAddrHi => 0x13;

        private FileStream stream;
        private long totalSectors;

        private readonly Memory registers;
        private bool interruptAsserted;
        private AtaCommand? activeCommand;

        public AtaDevice()
        {
            registers = new Memory((RegStatus_Command + 1) * 2);
        }

        public override void Initialize()
        {
            stream = File.Open(Config.DiskPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            totalSectors = stream.Length / Config.BytesPerSector;
        }

        public override void Shutdown()
        {
            stream.Dispose();
        }

        public override bool Tick()
        {
            if (activeCommand != null)
            {
                try
                {
                    activeCommand.Tick();
                }
                catch
                {
                    StatusFlags |= SR_ERR;
                }
            }

            return interruptAsserted;
        }

        public override void WriteByte(ushort segment, ushort offset, byte value)
        {
            registers.WriteByte(segment, (ushort)(offset * 2), value);

            if (offset == RegAltStatus_DevControl)
            {
                if ((value & DCR_SRST) != 0)
                    interruptAsserted = false;
            }
            else if (offset == RegStatus_Command)
            {
                interruptAsserted = false;
                DispatchCommand(value);
            }
        }

        public override void WriteWord(ushort segment, ushort offset, ushort value)
        {
            WriteByte(segment, offset, (byte)value);

            if (offset == RegData)
                activeCommand?.WriteData(value);
        }

        public override byte ReadByte(ushort segment, ushort offset)
        {
            var value = registers.ReadByte(segment, (ushort)(offset * 2));

            if (offset == RegStatus_Command)
            {
                StatusFlags &= ~SR_DRQ;
                interruptAsserted = false;
            }

            return value;
        }

        public override ushort ReadWord(ushort segment, ushort offset)
        {
            if (offset == RegData)
                return activeCommand?.ReadData() ?? 0;

            return ReadByte(segment, offset);
        }

        private void DispatchCommand(byte commandId)
        {
            // TODO Verify this behavior
            if ((StatusFlags & SR_BSY) != 0)
                return;

            StatusFlags &= ~SR_ERR;

            switch (commandId)
            {
                case 0x20: // Read sector(s) w/ verify
                case 0x21: // Read sector(s) w/o verify
                    activeCommand = new ReadSectorsCommand(this);
                    break;

                case 0x30: // Write sector(s) w/ verify
                case 0x31: // Write sector(s) w/o verify
                    activeCommand = new WriteSectorsCommand(this);
                    break;

                case 0xEC: // Identify drive
                    activeCommand = new IdentifyDriveCommand(this);
                    break;

                default:
                    StatusFlags |= SR_ERR;
                    break;
            }
        }

        private int ReadLBA()
        {
            var lba3 = ReadByte(0, RegLBA3);
            var lba2 = ReadByte(0, RegLBA2);
            var lba1 = ReadByte(0, RegLBA1);
            var lba0 = ReadByte(0, RegLBA0);

            int driveId = (lba3 & 0b0001_0000) >> 4; // TODO Discard if driveId does not match
            if ((lba3 & 0b0100_0000) == 0)
            {
                // CHS
                var c = (lba2 << 8) | lba1;
                var h = lba3 & 0x0F;
                var s = lba0;
                CHSToLBA(c, h, s, out var lba);
                return lba;
            }
            else
            {
                // LBA
                return ((lba3 & 0x0F) << 24) | (lba2 << 16) | (lba1 << 8) | lba0;
            }
        }

        private int ReadSectorCount()
        {
            int count = ReadByte(0, RegSectorCount);
            if (count == 0) count = 256;
            return count;
        }

        private void LBAToCHS(int lba, out int c, out int h, out int s)
        {
            var hpc = Config.HeadsPerCylinder;
            var spt = Config.SectorsPerTrack;
            c = lba / (hpc * spt);
            h = (lba / spt) % hpc;
            s = (lba % spt) + 1;
        }

        private void CHSToLBA(int c, int h, int s, out int lba)
        {
            var hpc = Config.HeadsPerCylinder;
            var spt = Config.SectorsPerTrack;
            lba = (c * hpc + h) * spt + (s - 1);
        }

        private void WriteSectors(long sector, int count, byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (count * Config.BytesPerSector != data.Length) throw new ArgumentException("data must be sector size * count");
            if (sector + count >= totalSectors) throw new ArgumentException("Would overwrite disk bounds");

            stream.Position = sector * Config.BytesPerSector;
            stream.Write(data, 0, count * Config.BytesPerSector);
            stream.Flush(true);
        }

        private byte[] ReadSectors(long sector, int count)
        {
            var size = count * Config.BytesPerSector;
            var buffer = new byte[size];

            stream.Position = sector * Config.BytesPerSector;
            if (stream.Read(buffer, 0, size) != size)
                throw new InvalidDataException();

            return buffer;
        }

        public sealed class ConfigDefinition : DeviceConfigBase
        {
            public string? DiskPath { get; set; }

            public int BytesPerSector { get; set; } = 512;

            public int HeadsPerCylinder { get; set; } = 16;

            public int SectorsPerTrack { get; set; } = 63;
        }

        private abstract class AtaCommand
        {
            protected AtaDevice Device { get; }

            protected AtaCommand(AtaDevice device)
            {
                Device = device ?? throw new ArgumentNullException(nameof(device));
            }

            public abstract void Tick();

            public abstract ushort ReadData();

            public abstract void WriteData(ushort value);
        }

        private sealed class ReadSectorsCommand : AtaCommand
        {
            private byte[]? pending;
            private int pendingIdx;

            private int stage;

            private readonly int lba, count;

            public ReadSectorsCommand(AtaDevice device)
                : base(device)
            {
                lba = device.ReadLBA();
                count = device.ReadSectorCount();
            }

            public override void Tick()
            {
                switch (stage)
                {
                    case 0: // 10.1.c
                        Device.StatusFlags |= SR_BSY;
                        stage = 1;
                        break;

                    case 1: // 10.1.c
                        // TODO Rate limit read to tick time
                        pending = Device.ReadSectors(lba, count);
                        pendingIdx = 0;
                        stage = 2;
                        break;

                    case 2: // 10.1.d
                        Device.StatusFlags &= ~SR_BSY;
                        Device.StatusFlags |= SR_DRQ;
                        Device.interruptAsserted = true;
                        stage = 3;
                        break;

                    case 3: // Complete
                        break;
                }
            }

            public override ushort ReadData()
            {
                if (pending != null && pendingIdx < pending.Length)
                {
                    var value = (ushort)((pending[pendingIdx + 0] << 8) | pending[pendingIdx + 1]);
                    pendingIdx += 2;

                    return value;
                }

                // TODO ERR? Read while BSY is set, or read more than specified sectors worth of data

                return 0;
            }

            public override void WriteData(ushort value)
            {
                // NOTE Nothing to do?
            }
        }

        private sealed class WriteSectorsCommand : AtaCommand
        {
            private int stage;

            private readonly byte[] pending;
            private int pendingIdx;

            private int lba, count;

            public WriteSectorsCommand(AtaDevice device)
                : base(device)
            {
                lba = device.ReadLBA();
                count = device.ReadSectorCount();

                pending = new byte[Device.Config.BytesPerSector];
                stage = 0;
            }

            public override void Tick()
            {
                // TODO Rate limit write to tick time

                switch (stage)
                {
                    case 0: // Init (10.2.c)
                        Device.StatusFlags |= SR_DRQ;
                        pendingIdx = 0;
                        stage = 1;
                        break;

                    case 1: // Pending host (10.2.d)
                        break;
                    
                    case 2: // Write to disk (10.2.f)
                        Device.WriteSectors(lba, 1, pending);
                        Device.StatusFlags &= ~SR_BSY;
                        Device.interruptAsserted = true;
                        if (count == 0)
                            stage = 4;
                        else
                        {
                            ++lba; --count;
                            Device.StatusFlags |= SR_DRQ;
                            stage = 3;
                        }
                        break;

                    case 3: // Pending status register read (10.2.g)
                        if (!Device.interruptAsserted)
                        {
                            pendingIdx = 0;
                            stage = 1;
                        }
                        break;

                    case 4: // Complete
                        break;
                }
            }

            public override ushort ReadData()
            {
                // NOTE Nothing to do
                return 0;
            }

            public override void WriteData(ushort value)
            {
                if (pendingIdx < pending.Length)
                {
                    pending[pendingIdx + 0] = (byte)(value >> 8);
                    pending[pendingIdx + 1] = (byte)(value & 0xFF);
                    pendingIdx += 2;
                }

                if (pendingIdx >= pending.Length)
                {
                    Device.StatusFlags &= ~SR_DRQ;
                    Device.StatusFlags |= SR_BSY;
                    stage = 2;
                }
            }
        }

        private sealed class IdentifyDriveCommand : AtaCommand
        {
            private int stage;

            private readonly byte[] pending;
            private int pendingIdx;

            public IdentifyDriveCommand(AtaDevice device)
                : base(device)
            {
                const string SerialNumber = "A1B2C3D4E5F6G7H8I9J0";
                const string FirmareVersion = "1.00";
                const string ModelNumber = "COBALT1PATA0001";

                var spt = Device.Config.SectorsPerTrack;
                var hpc = Device.Config.HeadsPerCylinder;
                var bps = Device.Config.BytesPerSector;

                using var stream = new MemoryStream();
                using var writer = new BinaryWriter(stream);

                writer.Write((ushort)0x0C5A);
                writer.Write((ushort)(device.totalSectors / spt / hpc));
                writer.Write((ushort)0x0000);
                writer.Write((ushort)hpc);
                writer.Write((ushort)(spt * bps));
                writer.Write((ushort)bps);
                writer.Write((ushort)spt);
                writer.Write((ushort)0x0000);
                writer.Write((ushort)0x0000);
                writer.Write((ushort)0x0000);
                WriteASCII16(SerialNumber, 20);
                writer.Write((ushort)0x0001);
                writer.Write((ushort)0x0000);
                writer.Write((ushort)0x0000);
                writer.Write((ushort)0x0000);
                WriteASCII16(FirmareVersion, 8);
                WriteASCII16(ModelNumber, 40);
                writer.Write((ushort)0x0000); // TODO Support multiple read/write commands?
                writer.Write((ushort)0x0000);
                writer.Write((ushort)0x0200); // TODO Support DMA some day...
                writer.Write((ushort)0x0000);
                writer.Write((ushort)0x0000);
                writer.Write((ushort)0x0000);
                writer.Write((ushort)0x0001);
                writer.Write((ushort)(device.totalSectors / spt));
                writer.Write((ushort)hpc);
                writer.Write((ushort)spt);
                writer.Write((ushort)(device.totalSectors >> 16) & 0xFFFF);
                writer.Write((ushort)(device.totalSectors & 0xFFFF));
                writer.Write((ushort)0x00FF);
                writer.Write((ushort)0x00FF);
                writer.Write((ushort)0xFFFF);
                writer.Write((ushort)0x0000);
                writer.Write((ushort)0x0000);

                pending = stream.ToArray();

                void WriteASCII16(string value, int length)
                {
                    for (int i = 0; i < length; ++i)
                    {
                        var ch0 = i + 0 < value.Length ? value[i + 0] : ' ';
                        var ch1 = i + 1 < value.Length ? value[i + 1] : ' ';
                        writer.Write((ushort)((ch0 << 8) | ch1));
                    }
                }
            }

            public override void Tick()
            {
                switch (stage)
                {
                    case 0: // 9.9
                        Device.StatusFlags |= SR_BSY;
                        stage = 1;
                        break;

                    case 1:
                        Device.StatusFlags &= ~SR_BSY;
                        Device.StatusFlags |= SR_DRQ;
                        Device.interruptAsserted = true;
                        stage = 2;
                        break;
                        
                    case 2: // Complete
                        break;
                }
            }

            public override ushort ReadData()
            {
                if (stage >= 1 && pendingIdx < pending.Length)
                {
                    var value = (ushort)((pending[pendingIdx + 0] << 8) | pending[pendingIdx + 1]);
                    pendingIdx += 2;

                    return value;
                }
                
                return 0;
            }

            public override void WriteData(ushort value)
            {
                // NOTE Nothing to do
            }
        }
    }
}
