using SDL2;

namespace Emulator
{
    public abstract class DeviceBase<TConfig> : IDeviceBase
        where TConfig : DeviceConfigBase, new()
    {
        public Machine Machine => ((IDeviceBase)this).Machine;

        public TConfig Config => (TConfig)((IDeviceBase)this).Config;

        Machine IDeviceBase.Machine { get; set; }

        DeviceConfigBase IDeviceBase.Config { get; set; }

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
        Machine Machine { get; set; }

        DeviceConfigBase Config { get; set; }

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

    public static class DeviceManager
    {
        private static IReadOnlyList<TypeMetadata>? deviceTypes;
        public static IReadOnlyList<TypeMetadata> GetDeviceTypes()
        {
            if (deviceTypes == null)
            {
                var deviceBaseType = typeof(IDeviceBase);
                var asm = deviceBaseType.Assembly;
                var types = asm.GetTypes();
                deviceTypes = types.Where(x => deviceBaseType.IsAssignableFrom(x)
                                            && x.BaseType != null
                                            && x.BaseType != typeof(object))
                    .Select(x => new TypeMetadata(x, x.BaseType!.GenericTypeArguments[0]))
                    .ToList();
            }

            return deviceTypes;
        }

        public sealed record TypeMetadata(Type Device, Type Config);
    }
}
