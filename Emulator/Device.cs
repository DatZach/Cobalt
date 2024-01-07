using SDL2;

namespace Emulator
{
    public abstract class Device
    {
        public Machine Machine { get; init; }

        public abstract string Name { get; }

        public abstract Memory? Memory { get; }

        public abstract short DevAddrLo { get; }

        public abstract short DevAddrHi { get; }

        public abstract void Initialize();

        public abstract void Shutdown();

        public abstract bool Tick();

        public abstract void DispatchEvent(SDL.SDL_Event ev);
    }
}
