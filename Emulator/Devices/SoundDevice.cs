using System.Runtime.InteropServices;
using SDL2;

namespace Emulator.Devices
{
    internal sealed class SoundDevice : DeviceBase<SoundDevice.SoundConfig>
    {
        public override string Name => "Sound";

        public override short DevAddrLo => 0x14;

        public override short DevAddrHi => 0x14;

        private bool interruptAsserted;
        private int tickIdx;
        private int bufferIdx;
        private readonly byte[] buffer;
        private IntPtr audioStream;
        private uint audioDevice;

        private readonly SDL.SDL_AudioCallback deviceAudioCallback;

        public SoundDevice()
        {
            deviceAudioCallback = AudioDevice_OnCallback;
            buffer = new byte[22050 * 2];
        }

        public override void Initialize()
        {
            //var audioSpec = new SDL.SDL_AudioSpec
            //{
            //    freq = 44100,
            //    format = SDL.AUDIO_F32,
            //    channels = 2,
            //    samples = 44100,
            //    callback = deviceAudioCallback
            //};
            var audioSpec = new SDL.SDL_AudioSpec
            {
                freq = 22050,
                format = SDL.AUDIO_U8,
                channels = 1,
                samples = 22050,
                callback = deviceAudioCallback
            };
            audioDevice = SDL.SDL_OpenAudioDevice(IntPtr.Zero, 0, ref audioSpec, out _, 0);

            audioStream = SDL.SDL_NewAudioStream(SDL.AUDIO_U8, 1, 22050, SDL.AUDIO_U8, 1, 22050);
            
            SDL.SDL_PauseAudioDevice(audioDevice, 0);
        }

        private void AudioDevice_OnCallback(IntPtr userdata, IntPtr outStream, int len)
        {
            SDL.SDL_AudioStreamGet(audioStream, outStream, len);
        }

        public override void Shutdown()
        {
            SDL.SDL_FreeAudioStream(audioStream);
        }

        public override unsafe bool Tick()
        {
            if (tickIdx++ >= 45 / 2) // TODO 45.35 ticks at the 1MHz rate, this is slightly slow
            {
                if (bufferIdx == 0 || bufferIdx == 22050)
                {
                    var pinBuffer = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                    SDL.SDL_LockAudioDevice(audioDevice);
                    SDL.SDL_AudioStreamPut(audioStream, pinBuffer.AddrOfPinnedObject() + (bufferIdx + 22050) % buffer.Length, 22050);
                    SDL.SDL_UnlockAudioDevice(audioDevice);
                    pinBuffer.Free();

                    interruptAsserted = true;
                }

                bufferIdx = (bufferIdx + 1) % buffer.Length;
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
                interruptAsserted = false;
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
                    return (byte)(interruptAsserted ? 0x01 : 0x00);
            }

            return 0;
        }

        public override ushort ReadWord(ushort segment, ushort offset)
        {
            return ReadByte(segment, offset);
        }

        public sealed class SoundConfig : DeviceConfigBase
        {

        }
    }
}
