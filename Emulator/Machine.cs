using System.Diagnostics;
using System.Text;
using Emulator.Devices;
using SDL2;

namespace Emulator
{
    public sealed class Machine
    {
        public int DEBUG_FPS { get; set; }

        public int ClockHz { get; set; }

        public int TickIndex { get; set; }

        public bool ShutdownWhenHalted { get; set; }

        public bool DebugOutput { get; set; }

        public bool IsPowered { get; private set; }

        public bool IsInterruptAsserted { get; private set; }

        public CPU CPU { get; }

        private Memory RAM { get; }

        private Memory ROM { get; }

        private readonly List<IDeviceBase> devices;
        public IReadOnlyList<IDeviceBase> Devices => devices;

        public Machine(MicrocodeRom microcode, Memory rom, IReadOnlyList<DeviceConfigBase> deviceConfigs)
        {
            ClockHz = 1_000_000;
            TickIndex = 0;
            RAM = new Memory(16 * 1024 * 1024);
            CPU = new CPU(this, microcode);
            ROM = rom;
            ROM.IsReadOnly = true;
            devices = new List<IDeviceBase>();

            var deviceTypes = DeviceManager.GetDeviceTypes();
            foreach (var deviceType in deviceTypes)
            {
                var deviceConfigType = deviceType.Config;
                var deviceConfig = deviceConfigs.FirstOrDefault(x => x.GetType() == deviceConfigType);
                if (deviceConfig == null)
                    deviceConfig = Activator.CreateInstance(deviceConfigType) as DeviceConfigBase;

                var device = Activator.CreateInstance(deviceType.Device) as IDeviceBase;

                if (device == null || deviceConfig == null)
                    throw new InvalidOperationException();

                device.Machine = this;
                device.Config = deviceConfig;
                devices.Add(device);
            }

            IsPowered = true;
        }
        
        public T GetDevice<T>()
            where T : IDeviceBase
        {
            for (int i = devices.Count - 1; i >= 0; --i)
            {
                if (devices[i] is T t)
                    return t;
            }

            throw new KeyNotFoundException($"No device '{typeof(T).Name}' found");
        }

        public void Initialize()
        {
            SDL.SDL_SetHint(SDL.SDL_HINT_RENDER_VSYNC, "0");
            SDL.SDL_SetHint(SDL.SDL_HINT_FRAMEBUFFER_ACCELERATION, "1");

            if (SDL.SDL_Init(SDL.SDL_INIT_VIDEO | SDL.SDL_INIT_AUDIO | SDL.SDL_INIT_EVENTS) < 0)
                throw new Exception($"SDL Init Error - {SDL.SDL_GetError()}");

            foreach (var device in devices)
                device.Initialize();
        }

        public void Shutdown()
        {
            foreach (var device in devices)
                device.Shutdown();

            SDL.SDL_Quit();
        }

        public void Run_Uncapped()
        {
            int machineTicks = 0;

            var thread = new Thread(() =>
            {
                var sw = Stopwatch.StartNew();
                while (true)
                {
                    var ms = sw.ElapsedMilliseconds;
                    if (ms >= 1000)
                    {
                        Console.WriteLine($"[CPU] {machineTicks:N0} Ticks; FPS {DEBUG_FPS}; Period {ms}ms");
                        DEBUG_FPS = 0;
                        machineTicks = 0;
                        sw.Restart();
                    }

                    Thread.Sleep(10);
                }
            });
            thread.Start();

            while (IsPowered)
            {
                Tick();
                ++machineTicks;
            }
        }

        public void Run()
        {
            int machineTicks = 0;

            //var thread = new Thread(() =>
            //{
            //    var sw = Stopwatch.StartNew();
            //    while (true)
            //    {
            //        var ms = sw.ElapsedMilliseconds;
            //        if (ms >= 1000)
            //        {
            //            Console.WriteLine($"[CPU] {machineTicks:N0} Ticks; FPS {DEBUG_FPS}; Period {ms}ms");
            //            DEBUG_FPS = 0;
            //            machineTicks = 0;
            //            sw.Restart();
            //        }

            //        //Thread.Sleep(10);
            //    }
            //});
            //thread.Start();

            const int TicksPerMs = 10000; // From Stopwatch class
            const int HzPerMs = 1000;
            var hzPerTick = TicksPerMs / (ClockHz / HzPerMs);
            double statAvgIterationsPerLoop = 0;
            double statTotalIterations = 0;
            double statTotalLoops = 0;
            double accumulator = 0.0;

            var sw = Stopwatch.StartNew();
            var lastTickTs = sw.ElapsedTicks;
            while (IsPowered) // && statTotalIterations < ClockHz)
            {
                ++statTotalLoops;
                var nowTickTs = sw.ElapsedTicks;
                var ticks = nowTickTs - lastTickTs;

                var dIterations = (double)ticks / hzPerTick;
                var iterations = (long)dIterations;
                accumulator += dIterations - iterations;
                if (accumulator >= 1.0)
                {
                    var lAccumulator = (long)accumulator;
                    iterations += lAccumulator;
                    accumulator -= lAccumulator;
                }
                if (iterations <= 0)
                    continue;

                statTotalIterations += iterations;
                statAvgIterationsPerLoop += iterations;
                while (iterations-- > 0 && IsPowered)
                {
                    Tick();
                    ++machineTicks;
                }

                lastTickTs = nowTickTs;
            }
            sw.Stop();

            statAvgIterationsPerLoop /= statTotalLoops;

            if (DebugOutput)
            {
                Console.WriteLine(
                    "STATS Ran {0}ms; AVG_ITR_PER_LOOP {1}; TOT_ITR {2}; TOT_LOOPS {3}; TICKS {4}",
                    sw.ElapsedMilliseconds,
                    statAvgIterationsPerLoop,
                    statTotalIterations,
                    statTotalLoops,
                    machineTicks
                );
            }
        }

        private void Tick()
        {
            var isCpuHalted = CPU.IsHalted;

            var canPollHostEvents = TickIndex % (ClockHz / 100) == 0;
            while (canPollHostEvents && SDL.SDL_PollEvent(out var ev) == 1)
            {
                if (ev.type == SDL.SDL_EventType.SDL_QUIT)
                {
                    IsPowered = false;
                    break;
                }

                foreach (var device in devices)
                    device.DispatchEvent(ev);
            }

            var isInterruptAsserted = false;
            for (var i = devices.Count - 1; i >= 0; --i)
                isInterruptAsserted |= devices[i].Tick();
            IsInterruptAsserted = isInterruptAsserted;

            CPU.Tick();

            ++TickIndex;

            if (!isCpuHalted && CPU.IsHalted)
            {
                if (ShutdownWhenHalted)
                    IsPowered = false;

                if (DebugOutput)
                {
                    Console.WriteLine("CPU Halted!");
                    Console.WriteLine(CaptureState());
                }
            }
        }
        
        public byte ReadByte(ushort segment, ushort offset)
        {
            SelectMemoryPage(ref segment, ref offset, out var device);
            return device.ReadByte(segment, offset);
        }

        public ushort ReadWord(ushort segment, ushort offset)
        {
            SelectMemoryPage(ref segment, ref offset, out var device);
            return device.ReadWord(segment, offset);
        }

        public void WriteByte(ushort segment, ushort offset, byte value)
        {
            SelectMemoryPage(ref segment, ref offset, out var device);
            device.WriteByte(segment, offset, value);
        }
        
        public void WriteWord(ushort segment, ushort offset, ushort value)
        {
            SelectMemoryPage(ref segment, ref offset, out var device);
            device.WriteWord(segment, offset, value);
        }

        private void SelectMemoryPage(ref ushort segment, ref ushort offset, out IMemory memory)
        {
            var segSel = segment & 0xC000;
            if (segment == 0x2000)
            {
                //segment -= 0x2000;
                memory = GetDevice<SoundDevice>();
            }
            else if (segSel == 0x0000)
                memory = RAM;
            else if (segSel == 0x4000)
            {
                segment -= 0x4000;
                memory = ROM;
            }
            else if (segSel == 0x8000)
            {
                segment -= 0x8000;
                memory = GetDevice<VideoDevice>();
            }
            else if (segSel == 0xC000)
            {
                foreach (var device in devices)
                {
                    if (offset < device.DevAddrLo || offset > device.DevAddrHi)
                        continue;

                    segment -= 0xC000;
                    offset -= (ushort)device.DevAddrLo;
                    memory = device;
                    return;
                }

                throw new InvalidOperationException($"Unknown device register 0x{offset:X4}");
            }
            else
                throw new InvalidOperationException($"Unknown segment address 0x{segment:X4}");
        }

        public MachineState CaptureState()
        {
            return new MachineState
            {
                CPU = CPU.CaptureState(),
                RAM = RAM.CaptureState()
            };
        }
    }

    public sealed record MachineState
    {
        public CpuState? CPU { get; init; }

        public Memory? RAM { get; init; }

        public Dictionary<ushort, ushort>? RAMChecks { get; init; } // For Unit Tests

        public bool IsEqual(MachineState? other)
        {
            if (other == null)
                return false;

            if (CPU != null && other.CPU != null)
            {
                var isEqual = true;
                isEqual = isEqual && (other.CPU.r0 == null || CPU.r0 == other.CPU.r0);
                isEqual = isEqual && (other.CPU.r1 == null || CPU.r1 == other.CPU.r1);
                isEqual = isEqual && (other.CPU.r2 == null || CPU.r2 == other.CPU.r2);
                isEqual = isEqual && (other.CPU.r3 == null || CPU.r3 == other.CPU.r3);
                isEqual = isEqual && (other.CPU.sp == null || CPU.sp == other.CPU.sp);
                isEqual = isEqual && (other.CPU.ss == null || CPU.ss == other.CPU.ss);
                isEqual = isEqual && (other.CPU.cs == null || CPU.cs == other.CPU.cs);
                isEqual = isEqual && (other.CPU.ds == null || CPU.ds == other.CPU.ds);
                isEqual = isEqual && (other.CPU.ip == null || CPU.ip == other.CPU.ip);
                isEqual = isEqual && (other.CPU.flags == null || CPU.flags == other.CPU.flags);
                if (!isEqual)
                    return false;
            }

            if (RAM != null && other.RAMChecks != null)
            {
                foreach (var kvp in other.RAMChecks)
                {
                    var actualWord = RAM.ReadWord(0x0000, kvp.Key);
                    if (actualWord != kvp.Value)
                        return false;
                }
            }

            return true;
        }

        public static implicit operator MachineState(CpuState cpu)
        {
            return new MachineState
            {
                CPU = cpu
            };
        }

        public override string ToString()
        {
            var sb = new StringBuilder();

            if (CPU != null)
            {
                sb.Append(CPU.ToString());
            }

            if (RAM != null)
            {
                if (CPU != null)
                    sb.AppendLine();
                sb.Append(RAM.ToString(0, 256));
            }

            if (RAMChecks != null)
            {
                if (CPU != null || RAM != null)
                    sb.AppendLine();
                foreach (var kvp in RAMChecks)
                    sb.AppendLine($"[{kvp.Key:X4}] = {kvp.Value:X4}");
            }

            return sb.ToString();
        }
    }
}
