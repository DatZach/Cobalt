using System.Text;

namespace Emulator
{
    public sealed class Machine
    {
        public bool ShutdownWhenHalted { get; set; }

        public bool DebugOutput { get; set; }

        public bool IsPowered { get; private set; }

        public CPU CPU { get; }

        public RAM RAM { get; }

        public Machine(MicrocodeRom microcode)
        {
            RAM = new RAM(16 * 1024 * 1024);
            CPU = new CPU(this, microcode);

            IsPowered = true;
        }

        public void Tick()
        {
            var isCpuHalted = CPU.IsHalted;

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

        public void Run()
        {
            // TODO Run at specified Hz
            while (IsPowered)
            {
                Tick();
            }
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

        public RAM? RAM { get; init; }

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
