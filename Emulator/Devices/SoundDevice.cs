using System.Runtime.InteropServices;
using SDL2;

namespace Emulator.Devices
{
    internal sealed class SoundDevice : DeviceBase<SoundDevice.SoundConfig>
    {
        public const int Frequency = 22050;
        public const int Channels = 1;
        public const int SampleSize = sizeof(byte);
        public const int BufferSize = Frequency * SampleSize * Channels;
        public const int BufferCount = 2;

        public override string Name => "Sound";

        public override short DevAddrLo => 0x14;

        public override short DevAddrHi => 0x14;

        private bool interruptAsserted;
        private bool isPaused;
        private int tickIdx;
        private int bufferIdx;
        private readonly byte[] buffer;
        private IntPtr inStream;
        private uint audioDeviceId;

        private readonly SDL.SDL_AudioCallback deviceAudioCallback;

        public SoundDevice()
        {
            deviceAudioCallback = AudioDevice_OnCallback;
            buffer = new byte[BufferSize * BufferCount];
            isPaused = true;
        }

        public override void Initialize()
        {
            var spec = new SDL.SDL_AudioSpec
            {
                freq = Frequency,
                format = SDL.AUDIO_U8,
                channels = Channels,
                samples = Frequency,
                callback = deviceAudioCallback
            };
            audioDeviceId = SDL.SDL_OpenAudioDevice(IntPtr.Zero, 0, ref spec, out _, 0);
            inStream = SDL.SDL_NewAudioStream(
                spec.format, spec.channels, spec.freq,
                spec.format, spec.channels, spec.freq
            );
            SDL.SDL_PauseAudioDevice(audioDeviceId, isPaused ? 1 : 0);
        }

        public override void Shutdown()
        {
            SDL.SDL_FreeAudioStream(inStream);
        }

        public override bool Tick()
        {
            var machine = Machine;
            if (!isPaused && machine.TickIndex % machine.ClockHz == 0)
            {
                Console.WriteLine($"[SoundDevice] Submitting buffer");

                var pinBuffer = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                SDL.SDL_LockAudioDevice(audioDeviceId);
                SDL.SDL_AudioStreamPut(
                    inStream,
                    pinBuffer.AddrOfPinnedObject() + bufferIdx * BufferSize,
                    BufferSize
                );
                SDL.SDL_UnlockAudioDevice(audioDeviceId);
                pinBuffer.Free();

                interruptAsserted = true;

                bufferIdx = (bufferIdx + 1) % BufferCount;
                tickIdx = 0;
            }

            return interruptAsserted;
        }

        public override void WriteByte(ushort segment, ushort offset, byte value)
        {
            if (segment == 0x2000)
            {
                if (offset > 0 && offset < buffer.Length)
                    buffer[offset] = value;
            }
            else if (segment == 0x0000)
            {
                var wasPaused = isPaused;
                isPaused = (value & 0x40) == 0x40;
                interruptAsserted = false;//wasPaused != isPaused && !isPaused;

                SDL.SDL_PauseAudioDevice(audioDeviceId, isPaused ? 1 : 0);

                Console.WriteLine($"[SoundDevice] WriteByte Status = {value:X2}; isPaused = {isPaused}");
            }
        }

        public override void WriteWord(ushort segment, ushort offset, ushort value)
        {
            WriteByte(segment, offset, (byte)value);
        }

        public override byte ReadByte(ushort segment, ushort offset)
        {
            if (segment == 0x2000)
            {
                if (offset > 0 && offset < buffer.Length)
                    return buffer[offset];
            }
            else if (segment == 0x0000)
            {
                if (offset == 0)
                {
                    return (byte)(
                          (bufferIdx & 0x01)
                        | (isPaused ? 0x40 : 0x00)
                        | (interruptAsserted ? 0x80 : 0x00)
                    );
                }
            }

            return 0;
        }

        public override ushort ReadWord(ushort segment, ushort offset)
        {
            return ReadByte(segment, offset);
        }

        private void AudioDevice_OnCallback(IntPtr userdata, IntPtr outStream, int len)
        {
            SDL.SDL_AudioStreamGet(inStream, outStream, len);
        }

        public sealed class SoundConfig : DeviceConfigBase
        {

        }
    }
}
