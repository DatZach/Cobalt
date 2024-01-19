using System.Diagnostics;
using SDL2;

namespace Emulator
{
    internal sealed class VideoDevice : DeviceBase<VideoDevice.ConfigDefinition>
    {
        private const int ResolutionWidth = 640;
        private const int ResolutionHeight = 480;
        private const int ScreenHz = 60;
        private const int Scale = 2;
        private const int GlyphWidth = 8;
        private const int GlyphHeight = 12;
        private const int LinesPerPage = ResolutionHeight / GlyphHeight;
        private const int GlyphsPerLine = ResolutionWidth / GlyphWidth;
        private const int Stride = 2;
        private const int UserDefinedGlyphIndex = 0xF0;

        public override string Name => "Video";
        
        public override short DevAddrLo => -1;
        public override short DevAddrHi => -1;

        private DateTime lastFrameTime;
        private IntPtr window;
        private IntPtr renderer;
        private IntPtr texFont;

        private readonly Memory memory;

        public VideoDevice()
        {
            memory = new Memory(0xFFFF);
        }

        public override void Initialize()
        {
            if (Config.Headless)
                return;

            window = SDL.SDL_CreateWindow(
                "Cobalt",
                SDL.SDL_WINDOWPOS_UNDEFINED,
                SDL.SDL_WINDOWPOS_UNDEFINED,
                ResolutionWidth * Scale,
                ResolutionHeight * Scale,
                SDL.SDL_WindowFlags.SDL_WINDOW_SHOWN
            );
            if (window == IntPtr.Zero)
                throw new Exception($"SDL Init Error - {SDL.SDL_GetError()}");

            renderer = SDL.SDL_CreateRenderer(window, -1, SDL.SDL_RendererFlags.SDL_RENDERER_ACCELERATED);
            if (renderer == IntPtr.Zero)
                throw new Exception($"SDL Init Error - {SDL.SDL_GetError()}");

            var surfFont = SDL.SDL_LoadBMP("CobaltFont.bmp");
            texFont = SDL.SDL_CreateTextureFromSurface(renderer, surfFont);
        }

        public override void Shutdown()
        {
            if (Config.Headless)
                return;

            SDL.SDL_DestroyRenderer(renderer);
            SDL.SDL_DestroyWindow(window);
        }

        public override bool Tick()
        {
            if (Config.Headless)
                return false;

            var nowFrameTime = DateTime.UtcNow;
            if ((nowFrameTime - lastFrameTime).Milliseconds < 1000 / ScreenHz)
                return false;
            lastFrameTime = nowFrameTime;

            SDL.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 255);
            SDL.SDL_RenderClear(renderer);

            var srcRect = new SDL.SDL_Rect { y = 0, w = GlyphWidth, h = GlyphHeight };
            var dstRect = new SDL.SDL_Rect { w = GlyphWidth * Scale, h = GlyphHeight * Scale };
            for (int y = 0; y < LinesPerPage; ++y)
            {
                for (int x = 0; x < GlyphsPerLine; ++x)
                {
                    var offset = (ushort)((y * GlyphsPerLine + x) * Stride);
                    var value = ReadWord(0x0000, offset);
                    var ch = value & 0x00FF;
                    var fg = (value & 0x0F00) >> 8;
                    var bg = (value & 0xF000) >> 12;
                    byte r, g, b;

                    srcRect.x = ch * GlyphWidth;
                    dstRect.x = x * GlyphWidth * Scale;
                    dstRect.y = y * GlyphHeight * Scale;

                    IndexedColorToRGB((byte)bg, out r, out g, out b);
                    SDL.SDL_SetRenderDrawColor(renderer, r, g, b, 255);
                    SDL.SDL_RenderFillRect(renderer, ref dstRect);

                    if ((ch & 0xF0) != 0xF0)
                    {
                        IndexedColorToRGB((byte)fg, out r, out g, out b);
                        SDL.SDL_SetTextureColorMod(texFont, r, g, b);
                        SDL.SDL_RenderCopy(renderer, texFont, ref srcRect, ref dstRect);
                    }
                    else
                    {
                        for (int yy = 0; yy < GlyphHeight; ++yy)
                        {
                            for (int xx = 0; xx < GlyphWidth; ++xx)
                            {
                                offset = (ushort)((ch - UserDefinedGlyphIndex) * (GlyphWidth * GlyphHeight) + 0x1900);
                                var col = ReadByte(0x0000, offset);
                                PackedByteToRGB(col, out r, out g, out b);
                                SDL.SDL_SetRenderDrawColor(renderer, r, g, b, 255);
                                SDL.SDL_RenderDrawPoint(renderer, dstRect.x + xx, dstRect.y + yy);
                            }
                        }
                    }
                }
            }

            //RenderColorSpace();
            
            SDL.SDL_RenderPresent(renderer);

            return false;
        }

        public override void WriteByte(ushort segment, ushort offset, byte value)
        {
            memory.WriteByte(segment, offset, value);
        }

        public override void WriteWord(ushort segment, ushort offset, ushort value)
        {
            memory.WriteWord(segment, offset, value);
        }

        public override byte ReadByte(ushort segment, ushort offset)
        {
            return memory.ReadByte(segment, offset);
        }

        public override ushort ReadWord(ushort segment, ushort offset)
        {
            return memory.ReadWord(segment, offset);
        }

        [Conditional("DEBUG")]
        private void RenderColorSpace()
        {
            var srcRect = new SDL.SDL_Rect();
            srcRect.y = 12;
            srcRect.w = GlyphWidth;
            srcRect.h = GlyphHeight;
            var dstRect = new SDL.SDL_Rect();
            dstRect.w = GlyphWidth;
            dstRect.h = GlyphHeight;

            int yo = 32;
            int c = 0;
            for (int y = 0; y < 16; ++y)
            {
                for (int x = 0; x < 16; ++x)
                {
                    var v = c++;
                    if (v >= 256)
                        break;

                    PackedByteToRGB((byte)v, out var r, out var g, out var b);
                    SDL.SDL_SetRenderDrawColor(renderer, r, g, b, 255);
                    dstRect.x = x * 8;
                    dstRect.y = yo + y * 8;
                    dstRect.w = 8;
                    dstRect.h = 8;
                    SDL.SDL_RenderFillRect(renderer, ref dstRect);
                }
            }

            c = 0;
            for (; c <= 0xF; ++c)
            {
                IndexedColorToRGB((byte)c, out var r, out var g, out var b);
                SDL.SDL_SetRenderDrawColor(renderer, r, g, b, 255);
                dstRect.x = c * 8;
                dstRect.y = yo + 24 * 8;
                dstRect.w = 8;
                dstRect.h = 8;
                SDL.SDL_RenderFillRect(renderer, ref dstRect);
            }

            c = 0;
            for (; c <= 0b111; ++c)
            {
                // VRGB -> VR_VGG_VBB
                var v = (byte)(
                        (((c & 0b110) >> 1) << 6)
                      | (((c & 0b110) >> 1) << 4)
                      | (((c & 0b110) >> 1) << 1)
                );
                PackedByteToRGB((byte)v, out var r, out var g, out var b);
                SDL.SDL_SetRenderDrawColor(renderer, r, g, b, 255);
                dstRect.x = c * 8;
                dstRect.y = yo + 32 * 8;
                dstRect.w = 8;
                dstRect.h = 8;
                SDL.SDL_RenderFillRect(renderer, ref dstRect);
            }
        }

        private static void IndexedColorToRGB(byte idx, out byte r, out byte g, out byte b)
        {
            // VBGR -> VBB_VGG_VR
            var iv = (idx & 0b1000) >> 3;
            var ib = (idx & 0b0100) >> 2;
            var ig = (idx & 0b0010) >> 1;
            var ir = (idx & 0b0001) >> 0;
            var v = (byte)(
                  (iv << 7)
                | (ib << 6)
                | (ib << 5)
                | (iv << 4)
                | (ig << 3)
                | (ig << 2)
                | (ir << 1)
                | (ir << 0)
            );

            PackedByteToRGB(v, out r, out g, out b);
        }

        private static void PackedByteToRGB(byte v, out byte r, out byte g, out byte b)
        {
            r = (byte)((
                0.104f
                + ((v & 0b00_000_001) != 0 ? 0.080f : 0.0f)
                + ((v & 0b00_000_010) != 0 ? 0.157f : 0.0f)
                + ((v & 0b00_000_100) != 0 ? 0.319f : 0.0f)
            ) / 0.700f * 255);
            g = (byte)((
                0.104f
                + ((v & 0b00_001_000) != 0 ? 0.080f : 0.0f)
                + ((v & 0b00_010_000) != 0 ? 0.157f : 0.0f)
                + ((v & 0b00_100_000) != 0 ? 0.319f : 0.0f)
            ) / 0.700f * 255);
            b = (byte)((
                0.104f
                + ((v & 0b01_000_000) != 0 ? 0.180f : 0.0f)
                + ((v & 0b10_000_000) != 0 ? 0.373f : 0.0f)
            ) / 0.700f * 255);
        }

        public sealed class ConfigDefinition : DeviceConfigBase
        {
            public bool Headless { get; set; } = false;
        }
    }
}
