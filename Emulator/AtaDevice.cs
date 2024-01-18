namespace Emulator
{
    internal sealed class AtaDevice : Device
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
            get => registers.ReadByte(0, RegStatus_Command * 2);
            set
            {
                registers.WriteByte(0, RegStatus_Command * 2, (byte)value);
                registers.WriteByte(0, RegAltStatus_DevControl * 2, (byte)value);
            }
        }

        public override string Name => "ATA Storage";

        public override short DevAddrLo => 0x0B;
        public override short DevAddrHi => 0x13;

        private FileStream stream;
        private long totalSectors;
        private int bytesPerSector;

        private readonly Memory registers;
        private bool interruptAsserted;
        private AtaCommand? activeCommand;

        public AtaDevice()
        {
            registers = new Memory((RegStatus_Command + 1) * 2);
        }

        public override void Initialize()
        {
            var path = @"C:\Temp\c.img"; // TODO From config
            bytesPerSector = 512; // TODO From config

            stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            totalSectors = stream.Length / bytesPerSector;
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
                {
                    var lba = (ReadByte(0, RegLBA3) << 24) | (ReadByte(0, RegLBA2) << 16) |
                              (ReadByte(0, RegLBA1) << 8 ) | (ReadByte(0, RegLBA0));
                    var count = (int)ReadByte(0, RegSectorCount);
                    if (count == 0) count = 256;

                    activeCommand = new ReadSectorsCommand(this, lba, count);
                    break;
                }

                case 0x30: // Write sector(s) w/ verify
                case 0x31: // Write sector(s) w/o verify
                {
                    var lba = (ReadByte(0, RegLBA3) << 24) | (ReadByte(0, RegLBA2) << 16) |
                              (ReadByte(0, RegLBA1) << 8 ) | (ReadByte(0, RegLBA0));
                    var count = (int)ReadByte(0, RegSectorCount);
                    if (count == 0) count = 256;

                    activeCommand = new WriteSectorsCommand(this, lba, count);
                    break;
                }

                case 0xEC: // Identify drive
                    activeCommand = new IdentifyDriveCommand(this);
                    break;

                default:
                    StatusFlags |= SR_ERR;
                    break;
            }
        }

        private void WriteSectors(long sector, int count, byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (count * bytesPerSector != data.Length) throw new ArgumentException("data must be sector size * count");
            if (sector + count >= totalSectors) throw new ArgumentException("Would overwrite disk bounds");

            stream.Position = sector * bytesPerSector;
            stream.Write(data, 0, count * bytesPerSector);
            stream.Flush(true);
        }

        private byte[] ReadSectors(long sector, int count)
        {
            var size = count * bytesPerSector;
            var buffer = new byte[size];

            stream.Position = sector * bytesPerSector;
            if (stream.Read(buffer, 0, size) != size)
                throw new InvalidDataException();

            return buffer;
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

            public ReadSectorsCommand(AtaDevice device, int lba, int count)
                : base(device)
            {
                this.lba = lba;
                this.count = count;
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

            public WriteSectorsCommand(AtaDevice device, int lba, int count)
                : base(device)
            {
                this.pending = new byte[Device.bytesPerSector];
                this.lba = lba;
                this.count = count;

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
            public IdentifyDriveCommand(AtaDevice device)
                : base(device)
            {

            }

            public override void Tick()
            {
                
            }

            public override ushort ReadData()
            {
                // NOTE Nothing to do
                return 0;
            }

            public override void WriteData(ushort value)
            {
                // NOTE Nothing to do
            }
        }
    }
}
