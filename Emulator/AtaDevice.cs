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
        private byte StatusFlags
        {
            get => registers.ReadByte(0, RegStatus_Command * 2);
            set
            {
                registers.WriteByte(0, RegStatus_Command * 2, value);
                registers.WriteByte(0, RegAltStatus_DevControl * 2, value);
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
        private byte[]? pending;
        private int pendingIdx;

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
        }

        public override byte ReadByte(ushort segment, ushort offset)
        {
            var value = registers.ReadByte(segment, (ushort)(offset * 2));

            if (offset == RegStatus_Command)
            {
                StatusFlags = (byte)(StatusFlags & ~SR_DRQ);
                interruptAsserted = false;
            }

            return value;
        }

        public override ushort ReadWord(ushort segment, ushort offset)
        {
            if (offset == RegData)
            {
                if (pending != null && pendingIdx < pending.Length)
                {
                    var value = (ushort)((pending[pendingIdx + 0] << 8) | pending[pendingIdx + 1]);
                    pendingIdx += 2;

                    return value;
                }

                return 0;
            }

            return ReadByte(segment, offset);
        }

        private void DispatchCommand(byte commandId)
        {
            switch (commandId)
            {
                case 0x20: // Read sector(s) w/ verify
                case 0x21: // Read sector(s) w/o verify
                {
                    var lba = (ReadByte(0, RegLBA3) << 24) | (ReadByte(0, RegLBA2) << 16) |
                              (ReadByte(0, RegLBA1) << 8 ) | (ReadByte(0, RegLBA0));
                    var count = (int)ReadByte(0, RegSectorCount);
                    if (count == 0) count = 256;

                    try
                    {
                        interruptAsserted = true;
                        // TODO BSY set?
                        pending = ReadSectors(lba, count); // TODO Process on a tick timescale?
                        pendingIdx = 0;
                        // TODO BSY unset?
                        StatusFlags |= SR_DRQ;
                    }
                    catch
                    {
                        StatusFlags |= SR_ERR;
                    }
                    break;
                }

                default:
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
    }
}
