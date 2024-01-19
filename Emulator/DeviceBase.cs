using SDL2;

namespace Emulator
{
    public abstract class DeviceBase<TConfig> : IDeviceBase
        where TConfig : DeviceConfigBase, new()
    {
        public Machine Machine { get; init; }

        public TConfig Config { get; init; }

        public abstract string Name { get; }

        public abstract short DevAddrLo { get; }

        public abstract short DevAddrHi { get; }

        public virtual void Initialize()
        {

        }

        public virtual void Shutdown()
        {

        }

        public virtual bool Tick()
        {
            return false;
        }

        public virtual void DispatchEvent(SDL.SDL_Event ev)
        {

        }

        public virtual byte ReadByte(ushort segment, ushort offset)
        {
            return 0;
        }

        public virtual ushort ReadWord(ushort segment, ushort offset)
        {
            return 0;
        }

        public virtual void WriteByte(ushort segment, ushort offset, byte value)
        {
            
        }

        public virtual void WriteWord(ushort segment, ushort offset, ushort value)
        {
            
        }
    }

    public interface IDeviceBase : IMemory
    {
        Machine Machine { get; init; }
        
        string Name { get; }

        short DevAddrLo { get; }

        short DevAddrHi { get; }

        void Initialize();
    
        void Shutdown();

        bool Tick();

        void DispatchEvent(SDL.SDL_Event ev);
    }

    public class DeviceConfigBase
    {

    }
}
