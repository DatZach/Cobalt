using SDL2;

namespace Emulator
{
    internal sealed class KeyboardDevice : Device
    {
        private const ushort RegScancode = 0x0000; // + 0x6000:0x0008

        public override string Name => "Keyboard";

        public override Memory? Memory { get; }
        public override short DevAddrLo => 0x08;
        public override short DevAddrHi => 0x08;

        private readonly byte[] buffer;
        private int bufferIdx;
        private int bufferHead;
        private DateTime tsLastDispatch;
        private bool interruptAsserted;

        public KeyboardDevice()
        {
            Memory = new Memory(1);
            Memory!.OnRead += Register_OnRead;
            buffer = new byte[16];
        }

        private void Register_OnRead(ushort segment, ushort offset, byte size)
        {
            if (offset == RegScancode)
                interruptAsserted = false;
        }

        public override void Initialize()
        {
            
        }

        public override void Shutdown()
        {
            
        }

        public override bool Tick()
        {
            if (bufferIdx != bufferHead)
            {
                // NOTE Keyboard will transmit a scancode byte every 600-1100ns, to keep things simple
                //      we can dispatch a scancode every 1000ns
                var tsNow = DateTime.UtcNow;
                if ((tsNow - tsLastDispatch).Milliseconds >= 1)
                {
                    var idx = bufferIdx++ % buffer.Length;
                    Memory!.WriteByte(0, RegScancode, buffer[idx]);
                    tsLastDispatch = DateTime.UtcNow;
                }
            }

            return interruptAsserted;
        }

        public override void DispatchEvent(SDL.SDL_Event ev)
        {
            Dictionary<SDL.SDL_Scancode, long>? table;
            if (ev.type == SDL.SDL_EventType.SDL_KEYDOWN)
                table = SDLScancode2ATScancode_Pressed;
            else if (ev.type == SDL.SDL_EventType.SDL_KEYUP)
                table = SDLScancode2ATScancode_Released;
            else
                return;

            if (!table.TryGetValue(ev.key.keysym.scancode, out var mapped))
                return;

            byte scancode;
            while ((scancode = (byte)(mapped & 0xFF)) != 0x00)
            {
                buffer[bufferHead++ % buffer.Length] = scancode;
                mapped >>= 8;
            }
            
            interruptAsserted = true;
        }

        private static readonly Dictionary<SDL.SDL_Scancode, long> SDLScancode2ATScancode_Pressed = new ()
        {
            [SDL.SDL_Scancode.SDL_SCANCODE_ESCAPE] = 0x01,
            [SDL.SDL_Scancode.SDL_SCANCODE_1] = 0x02,
            [SDL.SDL_Scancode.SDL_SCANCODE_2] = 0x03,
            [SDL.SDL_Scancode.SDL_SCANCODE_3] = 0x04,
            [SDL.SDL_Scancode.SDL_SCANCODE_4] = 0x05,
            [SDL.SDL_Scancode.SDL_SCANCODE_5] = 0x06,
            [SDL.SDL_Scancode.SDL_SCANCODE_6] = 0x07,
            [SDL.SDL_Scancode.SDL_SCANCODE_7] = 0x08,
            [SDL.SDL_Scancode.SDL_SCANCODE_8] = 0x09,
            [SDL.SDL_Scancode.SDL_SCANCODE_9] = 0x0A,
            [SDL.SDL_Scancode.SDL_SCANCODE_0] = 0x0B,
            [SDL.SDL_Scancode.SDL_SCANCODE_MINUS] = 0x0C,
            [SDL.SDL_Scancode.SDL_SCANCODE_EQUALS] = 0x0D,
            [SDL.SDL_Scancode.SDL_SCANCODE_BACKSPACE] = 0x0E,
            [SDL.SDL_Scancode.SDL_SCANCODE_TAB] = 0x0F,
            [SDL.SDL_Scancode.SDL_SCANCODE_Q] = 0x10,
            [SDL.SDL_Scancode.SDL_SCANCODE_W] = 0x11,
            [SDL.SDL_Scancode.SDL_SCANCODE_E] = 0x12,
            [SDL.SDL_Scancode.SDL_SCANCODE_R] = 0x13,
            [SDL.SDL_Scancode.SDL_SCANCODE_T] = 0x14,
            [SDL.SDL_Scancode.SDL_SCANCODE_Y] = 0x15,
            [SDL.SDL_Scancode.SDL_SCANCODE_U] = 0x16,
            [SDL.SDL_Scancode.SDL_SCANCODE_I] = 0x17,
            [SDL.SDL_Scancode.SDL_SCANCODE_O] = 0x18,
            [SDL.SDL_Scancode.SDL_SCANCODE_P] = 0x19,
            [SDL.SDL_Scancode.SDL_SCANCODE_LEFTBRACKET] = 0x1A,
            [SDL.SDL_Scancode.SDL_SCANCODE_RIGHTBRACKET] = 0x1B,
            [SDL.SDL_Scancode.SDL_SCANCODE_RETURN] = 0x1C,
            [SDL.SDL_Scancode.SDL_SCANCODE_LCTRL] = 0x1D,
            [SDL.SDL_Scancode.SDL_SCANCODE_A] = 0x1E,
            [SDL.SDL_Scancode.SDL_SCANCODE_S] = 0x1F,
            [SDL.SDL_Scancode.SDL_SCANCODE_D] = 0x20,
            [SDL.SDL_Scancode.SDL_SCANCODE_F] = 0x21,
            [SDL.SDL_Scancode.SDL_SCANCODE_G] = 0x22,
            [SDL.SDL_Scancode.SDL_SCANCODE_H] = 0x23,
            [SDL.SDL_Scancode.SDL_SCANCODE_J] = 0x24,
            [SDL.SDL_Scancode.SDL_SCANCODE_K] = 0x25,
            [SDL.SDL_Scancode.SDL_SCANCODE_L] = 0x26,
            [SDL.SDL_Scancode.SDL_SCANCODE_SEMICOLON] = 0x27,
            [SDL.SDL_Scancode.SDL_SCANCODE_APOSTROPHE] = 0x28,
            [SDL.SDL_Scancode.SDL_SCANCODE_GRAVE] = 0x29,
            [SDL.SDL_Scancode.SDL_SCANCODE_LSHIFT] = 0x2A,
            [SDL.SDL_Scancode.SDL_SCANCODE_BACKSLASH] = 0x2B,
            [SDL.SDL_Scancode.SDL_SCANCODE_Z] = 0x2C,
            [SDL.SDL_Scancode.SDL_SCANCODE_X] = 0x2D,
            [SDL.SDL_Scancode.SDL_SCANCODE_C] = 0x2E,
            [SDL.SDL_Scancode.SDL_SCANCODE_V] = 0x2F,
            [SDL.SDL_Scancode.SDL_SCANCODE_B] = 0x30,
            [SDL.SDL_Scancode.SDL_SCANCODE_N] = 0x31,
            [SDL.SDL_Scancode.SDL_SCANCODE_M] = 0x32,
            [SDL.SDL_Scancode.SDL_SCANCODE_COMMA] = 0x33,
            [SDL.SDL_Scancode.SDL_SCANCODE_PERIOD] = 0x34,
            [SDL.SDL_Scancode.SDL_SCANCODE_SLASH] = 0x35,
            [SDL.SDL_Scancode.SDL_SCANCODE_RSHIFT] = 0x36,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_MULTIPLY] = 0x37,
            [SDL.SDL_Scancode.SDL_SCANCODE_LEFT] = 0x38,
            [SDL.SDL_Scancode.SDL_SCANCODE_SPACE] = 0x39,
            [SDL.SDL_Scancode.SDL_SCANCODE_CAPSLOCK] = 0x3A,
            [SDL.SDL_Scancode.SDL_SCANCODE_F1] = 0x3B,
            [SDL.SDL_Scancode.SDL_SCANCODE_F2] = 0x3C,
            [SDL.SDL_Scancode.SDL_SCANCODE_F3] = 0x3D,
            [SDL.SDL_Scancode.SDL_SCANCODE_F4] = 0x3E,
            [SDL.SDL_Scancode.SDL_SCANCODE_F5] = 0x3F,
            [SDL.SDL_Scancode.SDL_SCANCODE_F6] = 0x40,
            [SDL.SDL_Scancode.SDL_SCANCODE_F7] = 0x41,
            [SDL.SDL_Scancode.SDL_SCANCODE_F8] = 0x42,
            [SDL.SDL_Scancode.SDL_SCANCODE_F9] = 0x43,
            [SDL.SDL_Scancode.SDL_SCANCODE_F10] = 0x44,
            [SDL.SDL_Scancode.SDL_SCANCODE_NUMLOCKCLEAR] = 0x45,
            [SDL.SDL_Scancode.SDL_SCANCODE_SCROLLLOCK] = 0x46,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_7] = 0x47,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_8] = 0x48,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_9] = 0x49,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_MINUS] = 0x4A,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_4] = 0x4B,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_5] = 0x4C,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_6] = 0x4D,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_PLUS] = 0x4E,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_1] = 0x4F,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_2] = 0x50,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_3] = 0x51,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_0] = 0x52,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_PERIOD] = 0x53,
            [SDL.SDL_Scancode.SDL_SCANCODE_F11] = 0x57,
            [SDL.SDL_Scancode.SDL_SCANCODE_F12] = 0x58,
            [SDL.SDL_Scancode.SDL_SCANCODE_MEDIASELECT] = 0x10E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_AUDIONEXT] = 0x19E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_ENTER] = 0x1CE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_RCTRL] = 0x1DE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_AUDIOMUTE] = 0x20E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_CALCULATOR] = 0x21E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_AUDIOPLAY] = 0x22E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_AUDIOSTOP] = 0x24E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_VOLUMEDOWN] = 0x2EE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_VOLUMEUP] = 0x30E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_WWW] = 0x32E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_DIVIDE] = 0x35E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_RALT] = 0x38E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_HOME] = 0x47E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_UP] = 0x48E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_PAGEUP] = 0x49E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_LEFT] = 0x4BE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_RIGHT] = 0x4DE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_END] = 0x4FE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_DOWN] = 0x50E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_PAGEDOWN] = 0x51E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_INSERT] = 0x52E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_DELETE] = 0x53E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_LGUI] = 0x5BE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_RGUI] = 0x5CE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_APPLICATION] = 0x5DE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_POWER] = 0x5EE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_SLEEP] = 0x5FE0,
            //[SDL.SDL_Scancode.SDL_SCANCODE_WAKE] = 0x63E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_AC_SEARCH] = 0x65E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_AC_BOOKMARKS] = 0x66E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_AC_REFRESH] = 0x67E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_AC_STOP] = 0x68E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_AC_FORWARD] = 0x69E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_AC_BACK] = 0x6AE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_COMPUTER] = 0x6BE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_MAIL] = 0x6CE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_MEDIASELECT] = 0x6DE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_PRINTSCREEN] = 0x37E02AE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_PAUSE] = 0xC59DE1451DE1,
        };

        private static readonly Dictionary<SDL.SDL_Scancode, long> SDLScancode2ATScancode_Released = new ()
        {
            [SDL.SDL_Scancode.SDL_SCANCODE_ESCAPE] = 0x81,
            [SDL.SDL_Scancode.SDL_SCANCODE_1] = 0x82,
            [SDL.SDL_Scancode.SDL_SCANCODE_2] = 0x83,
            [SDL.SDL_Scancode.SDL_SCANCODE_3] = 0x84,
            [SDL.SDL_Scancode.SDL_SCANCODE_4] = 0x85,
            [SDL.SDL_Scancode.SDL_SCANCODE_5] = 0x86,
            [SDL.SDL_Scancode.SDL_SCANCODE_6] = 0x87,
            [SDL.SDL_Scancode.SDL_SCANCODE_7] = 0x88,
            [SDL.SDL_Scancode.SDL_SCANCODE_8] = 0x89,
            [SDL.SDL_Scancode.SDL_SCANCODE_9] = 0x8A,
            [SDL.SDL_Scancode.SDL_SCANCODE_0] = 0x8B,
            [SDL.SDL_Scancode.SDL_SCANCODE_MINUS] = 0x8C,
            [SDL.SDL_Scancode.SDL_SCANCODE_EQUALS] = 0x8D,
            [SDL.SDL_Scancode.SDL_SCANCODE_BACKSPACE] = 0x8E,
            [SDL.SDL_Scancode.SDL_SCANCODE_TAB] = 0x8F,
            [SDL.SDL_Scancode.SDL_SCANCODE_Q] = 0x90,
            [SDL.SDL_Scancode.SDL_SCANCODE_W] = 0x91,
            [SDL.SDL_Scancode.SDL_SCANCODE_E] = 0x92,
            [SDL.SDL_Scancode.SDL_SCANCODE_R] = 0x93,
            [SDL.SDL_Scancode.SDL_SCANCODE_T] = 0x94,
            [SDL.SDL_Scancode.SDL_SCANCODE_Y] = 0x95,
            [SDL.SDL_Scancode.SDL_SCANCODE_U] = 0x96,
            [SDL.SDL_Scancode.SDL_SCANCODE_I] = 0x97,
            [SDL.SDL_Scancode.SDL_SCANCODE_O] = 0x98,
            [SDL.SDL_Scancode.SDL_SCANCODE_P] = 0x99,
            [SDL.SDL_Scancode.SDL_SCANCODE_LEFTBRACKET] = 0x9A,
            [SDL.SDL_Scancode.SDL_SCANCODE_RIGHTBRACKET] = 0x9B,
            [SDL.SDL_Scancode.SDL_SCANCODE_RETURN] = 0x9C,
            [SDL.SDL_Scancode.SDL_SCANCODE_LCTRL] = 0x9D,
            [SDL.SDL_Scancode.SDL_SCANCODE_A] = 0x9E,
            [SDL.SDL_Scancode.SDL_SCANCODE_S] = 0x9F,
            [SDL.SDL_Scancode.SDL_SCANCODE_D] = 0xA0,
            [SDL.SDL_Scancode.SDL_SCANCODE_F] = 0xA1,
            [SDL.SDL_Scancode.SDL_SCANCODE_G] = 0xA2,
            [SDL.SDL_Scancode.SDL_SCANCODE_H] = 0xA3,
            [SDL.SDL_Scancode.SDL_SCANCODE_J] = 0xA4,
            [SDL.SDL_Scancode.SDL_SCANCODE_K] = 0xA5,
            [SDL.SDL_Scancode.SDL_SCANCODE_L] = 0xA6,
            [SDL.SDL_Scancode.SDL_SCANCODE_SEMICOLON] = 0xA7,
            [SDL.SDL_Scancode.SDL_SCANCODE_APOSTROPHE] = 0xA8,
            [SDL.SDL_Scancode.SDL_SCANCODE_GRAVE] = 0xA9,
            [SDL.SDL_Scancode.SDL_SCANCODE_LSHIFT] = 0xAA,
            [SDL.SDL_Scancode.SDL_SCANCODE_BACKSLASH] = 0xAB,
            [SDL.SDL_Scancode.SDL_SCANCODE_Z] = 0xAC,
            [SDL.SDL_Scancode.SDL_SCANCODE_X] = 0xAD,
            [SDL.SDL_Scancode.SDL_SCANCODE_C] = 0xAE,
            [SDL.SDL_Scancode.SDL_SCANCODE_V] = 0xAF,
            [SDL.SDL_Scancode.SDL_SCANCODE_B] = 0xB0,
            [SDL.SDL_Scancode.SDL_SCANCODE_N] = 0xB1,
            [SDL.SDL_Scancode.SDL_SCANCODE_M] = 0xB2,
            [SDL.SDL_Scancode.SDL_SCANCODE_COMMA] = 0xB3,
            [SDL.SDL_Scancode.SDL_SCANCODE_PERIOD] = 0xB4,
            [SDL.SDL_Scancode.SDL_SCANCODE_SLASH] = 0xB5,
            [SDL.SDL_Scancode.SDL_SCANCODE_RSHIFT] = 0xB6,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_MULTIPLY] = 0xB7,
            [SDL.SDL_Scancode.SDL_SCANCODE_LEFT] = 0xB8,
            [SDL.SDL_Scancode.SDL_SCANCODE_SPACE] = 0xB9,
            [SDL.SDL_Scancode.SDL_SCANCODE_CAPSLOCK] = 0xBA,
            [SDL.SDL_Scancode.SDL_SCANCODE_F1] = 0xBB,
            [SDL.SDL_Scancode.SDL_SCANCODE_F2] = 0xBC,
            [SDL.SDL_Scancode.SDL_SCANCODE_F3] = 0xBD,
            [SDL.SDL_Scancode.SDL_SCANCODE_F4] = 0xBE,
            [SDL.SDL_Scancode.SDL_SCANCODE_F5] = 0xBF,
            [SDL.SDL_Scancode.SDL_SCANCODE_F6] = 0xC0,
            [SDL.SDL_Scancode.SDL_SCANCODE_F7] = 0xC1,
            [SDL.SDL_Scancode.SDL_SCANCODE_F8] = 0xC2,
            [SDL.SDL_Scancode.SDL_SCANCODE_F9] = 0xC3,
            [SDL.SDL_Scancode.SDL_SCANCODE_F10] = 0xC4,
            [SDL.SDL_Scancode.SDL_SCANCODE_NUMLOCKCLEAR] = 0xC5,
            [SDL.SDL_Scancode.SDL_SCANCODE_SCROLLLOCK] = 0xC6,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_7] = 0xC7,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_8] = 0xC8,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_9] = 0xC9,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_MINUS] = 0xCA,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_4] = 0xCB,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_5] = 0xCC,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_6] = 0xCD,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_PLUS] = 0xCE,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_1] = 0xCF,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_2] = 0xD0,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_3] = 0xD1,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_0] = 0xD2,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_PERIOD] = 0xD3,
            [SDL.SDL_Scancode.SDL_SCANCODE_F11] = 0xD7,
            [SDL.SDL_Scancode.SDL_SCANCODE_F12] = 0xD8,
            [SDL.SDL_Scancode.SDL_SCANCODE_MEDIASELECT] = 0x90E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_AUDIONEXT] = 0x99E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_ENTER] = 0x9CE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_RCTRL] = 0x9DE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_AUDIOMUTE] = 0xA0E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_CALCULATOR] = 0xA1E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_AUDIOPLAY] = 0xA2E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_AUDIOSTOP] = 0xA4E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_VOLUMEDOWN] = 0xAEE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_VOLUMEUP] = 0xB0E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_WWW] = 0xB2E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_KP_DIVIDE] = 0xB5E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_RALT] = 0xB8E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_HOME] = 0xC7E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_UP] = 0xC8E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_PAGEUP] = 0xC9E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_LEFT] = 0xCBE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_RIGHT] = 0xCDE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_END] = 0xCFE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_DOWN] = 0xD0E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_PAGEDOWN] = 0xD1E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_INSERT] = 0xD2E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_DELETE] = 0xD3E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_LGUI] = 0xDBE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_RGUI] = 0xDCE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_APPLICATION] = 0xDDE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_POWER] = 0xDEE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_SLEEP] = 0xDFE0,
            //[SDL.SDL_Scancode.SDL_SCANCODE_WAKE] = 0xE3E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_AC_SEARCH] = 0xE5E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_AC_BOOKMARKS] = 0xE6E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_AC_REFRESH] = 0xE7E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_AC_STOP] = 0xE8E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_AC_FORWARD] = 0xE9E0,
            [SDL.SDL_Scancode.SDL_SCANCODE_AC_BACK] = 0xEAE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_COMPUTER] = 0xEBE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_MAIL] = 0xECE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_MEDIASELECT] = 0xEDE0,
            [SDL.SDL_Scancode.SDL_SCANCODE_PRINTSCREEN] = 0xAAE0B7E0,
        };
    }
}
