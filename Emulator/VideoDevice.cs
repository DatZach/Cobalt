using SDL2;

namespace Emulator
{
    internal sealed class VideoDevice : Device
    {
        private const int ResolutionWidth = 640;
        private const int ResolutionHeight = 480;
        private const int Scale = 1;
        private const int GlyphWidth = 8;
        private const int GlyphHeight = 12;

        public override string Name => "Video";

        private IntPtr window;
        private IntPtr renderer;
        private IntPtr bmpFont;

        public override void Initialize()
        {
            if (SDL.SDL_Init(SDL.SDL_INIT_VIDEO) < 0)
                throw new Exception($"SDL Init Error - {SDL.SDL_GetError()}");

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

            var _bmpFont = SDL.SDL_LoadBMP("CobaltFont.bmp");
            bmpFont = SDL.SDL_CreateTextureFromSurface(renderer, _bmpFont);
        }

        public override void Shutdown()
        {
            SDL.SDL_DestroyRenderer(renderer);
            SDL.SDL_DestroyWindow(window);
            SDL.SDL_Quit();
        }

        public override bool Tick()
        {
            while (SDL.SDL_PollEvent(out var ev) == 1)
            {
                switch (ev.type)
                {
                    case SDL.SDL_EventType.SDL_QUIT:
                        Environment.Exit(0);
                        break;
                }
            }

            SDL.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 255);
            SDL.SDL_RenderClear(renderer);

            var srcRect = new SDL.SDL_Rect();
            srcRect.y = 12;
            srcRect.w = GlyphWidth;
            srcRect.h = GlyphHeight;
            var dstRect = new SDL.SDL_Rect();
            dstRect.w = GlyphWidth;
            dstRect.h = GlyphHeight;

            const int LinesPerPage = ResolutionHeight / GlyphHeight;
            const int GlyphsPerLine = ResolutionWidth / GlyphWidth;
            const int Stride = 2;
            for (int y = 0; y < LinesPerPage; ++y)
            {
                for (int x = 0; x < GlyphsPerLine; ++x)
                {
                    var offset = y * GlyphsPerLine + x;
                    var ch = Machine.ReadWord(0x0001, (ushort)(offset * Stride));
                    if (ch == 0)
                        continue;

                    srcRect.x = ch * GlyphWidth;
                    dstRect.x = x * GlyphWidth;
                    dstRect.y = y * GlyphHeight;
                    SDL.SDL_RenderCopy(renderer, bmpFont, ref srcRect, ref dstRect);
                }
            }

            SDL.SDL_RenderPresent(renderer);

            return false;
        }
    }
}
