﻿using System.Diagnostics;
using System.Text;

namespace Emulator
{
    public sealed class Machine
    {
        public int ClockHz { get; set; }

        public bool ShutdownWhenHalted { get; set; }

        public bool DebugOutput { get; set; }

        public bool IsPowered { get; private set; }

        public bool IsInterruptAsserted { get; private set; }

        public CPU CPU { get; }

        private Memory RAM { get; }

        private Memory ROM { get; }

        private readonly List<Device> devices;
        public IReadOnlyList<Device> Devices => devices;

        public Machine(MicrocodeRom microcode, Memory rom)
        {
            ClockHz = 1_000_000;
            RAM = new Memory(16 * 1024 * 1024);
            CPU = new CPU(this, microcode);
            ROM = rom;
            ROM.IsReadOnly = true;
            devices = new List<Device>();

            IsPowered = true;
        }

        public void AddDevice<T>()
            where T : Device, new()
        {
            var device = new T { Machine = this };
            devices.Add(device);
        }

        public T? GetDevice<T>()
            where T : Device
        {
            return devices.FirstOrDefault(x => x is T) as T;
        }

        public void Run()
        {
            const int TicksPerMs = 10000; // From Stopwatch class
            const int HzPerMs = 1000;
            var hzPerTick = TicksPerMs / (ClockHz / HzPerMs);
            double statAvgIterationsPerLoop = 0;
            double statTotalIterations = 0;
            double statTotalLoops = 0;

            foreach (var device in devices)
                device.Initialize();

            var sw = Stopwatch.StartNew();
            var lastTickTs = sw.ElapsedTicks;
            while (IsPowered) // && statTotalIterations < ClockHz)
            {
                ++statTotalLoops;
                var nowTickTs = sw.ElapsedTicks;
                var ticks = nowTickTs - lastTickTs;

                var iterations = ticks / hzPerTick;
                if (iterations <= 0)
                    continue;

                statTotalIterations += iterations;
                statAvgIterationsPerLoop += iterations;
                while (iterations-- > 0 && IsPowered)
                    Tick();

                lastTickTs = nowTickTs;
            }
            sw.Stop();

            statAvgIterationsPerLoop /= statTotalLoops;

            foreach (var device in devices)
                device.Shutdown();

            if (DebugOutput)
            {
                Console.WriteLine(
                    "STATS Ran {0}ms; AVG_ITR_PER_LOOP {1}; TOT_ITR {2}; TOT_LOOPS {3}",
                    sw.ElapsedMilliseconds,
                    statAvgIterationsPerLoop,
                    statTotalIterations,
                    statTotalLoops
                );
            }
        }

        private void Tick()
        {
            var isCpuHalted = CPU.IsHalted;

            IsInterruptAsserted = false;
            foreach (var device in devices)
                IsInterruptAsserted |= device.Tick();

            CPU.Tick();

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

        private void SelectMemoryPage(ref ushort segment, ref ushort offset, out Memory memory)
        {
            var segSel = segment & 0xC000;
            if (segSel == 0x0000)
                memory = RAM;
            else if (segSel == 0x4000)
            {
                segment -= 0x4000;
                memory = ROM;
            }
            else if (segSel == 0x8000)
            {
                segment -= 0x8000;
                memory = GetDevice<VideoDevice>()!.Memory!;
            }
            else if (segSel == 0xC000)
            {
                foreach (var device in devices)
                {
                    if (offset < device.DevAddrLo || offset > device.DevAddrHi)
                        continue;

                    segment -= 0xC000;
                    memory = device.Memory!;
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

        public Dictionary<ushort, short>? RAMChecks { get; init; } // For Unit Tests

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
