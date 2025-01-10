using Emulator;

namespace EmulatorTests
{
    [TestClass]
    public class InstructionTests
    {
        private readonly MicrocodeRom microcodeRom;

        public InstructionTests()
        {
            microcodeRom = Microcode.AssembleRom("Microcode3.cmc");
        }

        [TestMethod]
        public void MOV_REG_REG()
        {
            AssertState(
                @"
                mov r0, 0x1234
                mov r1, r0
                ",
                new CpuState
                {
                    r0 = 0x1234,
                    r1 = 0x1234
                }
            );
        }

        [TestMethod]
        public void MOV_REG_IMM8()
        {
            AssertState(
                @"
                mov r0, 0xFFFF
                mov r0, 0x12
                mov r1, 0x34
                mov r2h, 0x12
                mov r2l, 0x34
                mov r3h, 0x56
                mov sp, 0x0F
                mov ss, 0x80
                mov ds, 0x01
                ",
                new CpuState
                {
                    r0 = 0x0012,
                    r1 = 0x0034,
                    r2 = 0x1234,
                    r3 = 0x5600,
                    sp = 0x000F,
                    ss = 0x0080,
                    ds = 0x0001,
                }
            );
        }

        [TestMethod]
        public void MOV_REG_IMM16()
        {
            AssertState(
                @"
                mov r0, 0x1234
                mov r1, 0x4321
                mov r2, 0x1234
                mov r3, 0x4321
                mov sp, 0x1234
                mov ss, 0x4321
                mov ds, 0x0001
                mov cs, 0x4000
                ",
                new CpuState
                {
                    r0 = 0x1234,
                    r1 = 0x4321,
                    r2 = 0x1234,
                    r3 = 0x4321,
                    sp = 0x1234,
                    ss = 0x4321,
                    cs = 0x4000,
                    ds = 0x0001,
                }
            );
        }

        [TestMethod]
        public void MOV_REG_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov word[ds:0x90], 0x1234
                mov r0, 0x70
                mov r1, word[ds:r0+0x10]
                mov r2, byte[ds:r0+0x11]
                mov r0, 0xA0
                mov r3, word[ds:r0-0x10]
                mov r0, byte[ds:r0-0x0F]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0034,
                        r1 = 0x1234,
                        r2 = 0x0034,
                        r3 = 0x1234
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x90] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void MOV_REG_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r0, 0x80
                mov r1, word[ds:r0]
                mov r2, byte[ds:r0]
                mov r3h, byte[ds:r0]
                mov r0, 0x81
                mov r3l, byte[ds:r0]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0081,
                        r1 = 0x1234,
                        r2 = 0x0012,
                        r3 = 0x1234
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void MOV_REG_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r0, word[ds:0x80]
                mov r1, byte[ds:0x80]
                mov r2h, byte[ds:0x80]
                mov r2l, byte[ds:0x81]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234,
                        r1 = 0x0012,
                        r2 = 0x1234
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void MOV_sizeSEGREGplusIMM_REG()
        {
            AssertState(
                @"
                mov r0, 0x1234
                mov r1, 0x40
                mov word[ds:r1+0x10], r0
                mov byte[ds:r1+0x20], r0
                mov byte[ds:r1+0x30], r0h
                mov byte[ds:r1+0x31], r0l
                mov word[ds:r1-0x10], r0
                mov byte[ds:r1-0x20], r0
                mov byte[ds:r1-0x30], r0h
                mov byte[ds:r1-0x2F], r0l
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234,
                        r1 = 0x0040
                    },
                    RAMChecks = new()
                    {
                        [0x50] = 0x1234,
                        [0x60] = 0x3400,
                        [0x70] = 0x1234,
                        [0x30] = 0x1234,
                        [0x20] = 0x3400,
                        [0x10] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void MOV_sizeSEGREGplusIMM_IMM()
        {
            AssertState(
                @"
                mov r0, 0x1234
                mov r1, 0x40
                mov word[ds:r1+0x10], r0
                mov byte[ds:r1+0x20], r0
                mov byte[ds:r1+0x30], r0h
                mov byte[ds:r1+0x31], r0l
                mov word[ds:r1-0x10], r0
                mov byte[ds:r1-0x20], r0
                mov byte[ds:r1-0x30], r0h
                mov byte[ds:r1-0x2F], r0l
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234,
                        r1 = 0x0040
                    },
                    RAMChecks = new()
                    {
                        [0x50] = 0x1234,
                        [0x60] = 0x3400,
                        [0x70] = 0x1234,
                        [0x30] = 0x1234,
                        [0x20] = 0x3400,
                        [0x10] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void MOV_sizeSEGREGplusIMM_sizeSEGREG()
        {
            AssertState(
                @"
                mov r0, 0x80
                mov r1, 0x40
                mov word[ds:r0], 0x1234
                mov word[ds:r1+0x10], word[ds:r0]
                mov word[ds:r1+0x20], byte[ds:r0]
                mov byte[ds:r1+0x30], byte[ds:r0]
                mov word[ds:r1-0x10], word[ds:r0]
                mov word[ds:r1-0x20], byte[ds:r0]
                mov byte[ds:r1-0x30], byte[ds:r0]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0080,
                        r1 = 0x0040
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x50] = 0x1234,
                        [0x60] = 0x3400,
                        [0x70] = 0x1200,
                        [0x30] = 0x1234,
                        [0x20] = 0x3400,
                        [0x10] = 0x1200
                    }
                }
            );
        }

        [TestMethod]
        public void MOV_sizeSEGREGplusIMM_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov r0, 0x80
                mov r1, 0x40
                mov word[ds:r0], 0x1234
                mov word[ds:r1+0x10], word[ds:0x80]
                mov word[ds:r1+0x20], byte[ds:0x80]
                mov byte[ds:r1+0x30], byte[ds:0x80]
                mov word[ds:r1-0x10], word[ds:0x80]
                mov word[ds:r1-0x20], byte[ds:0x80]
                mov byte[ds:r1-0x30], byte[ds:0x80]
                mov word[ds:r1-0x40], word[0x0000:0x80]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0080,
                        r1 = 0x0040
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x50] = 0x1234,
                        [0x60] = 0x0012,
                        [0x70] = 0x1200,
                        [0x30] = 0x1234,
                        [0x20] = 0x0012,
                        [0x10] = 0x1200,
                        [0x00] = 0x1234
                    }
                }
            );
        }

        // TODO mov size[seg:reg+sImm], size[seg:reg+sImm]

        private void AssertState(string source, MachineState expectedState)
        {
            var devices = new List<DeviceConfigBase>();
            var bootRom = new Memory(0x0FFF);

            var assembler = new Assembler(microcodeRom);
            var program = assembler.AssembleSource(
                "nop\n" +
                "sie 0\n" +
                source
            );
            var hlt = assembler.AssembleSource("hlt");
            for (ushort i = 0; i < bootRom.Size; ++i)
            {
                var value = i < program.Length ? program[i] : hlt[0];
                bootRom.WriteByte(0, i, value);
            }

            var machine = new Machine(microcodeRom, bootRom, devices)
            {
                ShutdownWhenHalted = true,
                DebugOutput = true
            };

            machine.Run(); // machine.Run_Uncapped();

            var actualState = machine.CaptureState();
            if (!actualState.IsEqual(expectedState))
                Assert.Fail($"\nEXPECTED\n{expectedState}\n\nACTUAL\n{actualState}");
        }
    }
}