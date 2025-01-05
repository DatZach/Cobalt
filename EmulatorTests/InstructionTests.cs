using Emulator;

namespace EmulatorTests
{
    [TestClass]
    public class InstructionTests
    {
        private readonly MicrocodeRom microcodeRom;

        public InstructionTests()
        {
            microcodeRom = Microcode.AssembleRom("Microcode2.cmc");
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
        public void MOV_REG_dSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov word[ds:0x90], 0x1234
                mov r0, 0x80
                mov r1, word[ds:r0]
                mov r2, word:[ds:r0+0x10]
                mov r0, 0xA0
                mov r3, word:[ds:r0-0x10]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x00A0,
                        r1 = 0x1234,
                        r2 = 0x1234,
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
        public void MOV_REG_dIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r0, word[ds:0x80]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void MOV_dIMM16_REG()
        {
            AssertState(
                @"
                mov r0, 0x1234
                mov word[ds:0x80], r0
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void MOV_dIMM16_IMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                ",
                new MachineState
                {
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void MOV_dIMM16_dREGIMM16() // TODO VERIFY
        {
            AssertState(
                @"
                mov r0, 0x100
                mov word[ds:0x110], 0x1234
                mov word[ds:0x80], word[ds:r0+0x10]
                mov r0, 0x120
                mov word[ds:0x90], word[ds:r0-0x10]
                ",
                new MachineState
                {
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x90] = 0x1234,
                        [0x110] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void MOV_dIMM16_dIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x100], 0x1234
                mov word[ds:0x110], [word:0x100]
                ",
                new MachineState
                {
                    RAMChecks = new()
                    {
                        [0x100] = 0x1234,
                        [0x110] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void MOV_dREGIMM16_REG()
        {
            AssertState(
                @"
                mov r0, 0x100
                mov r1, 0x1234
                mov word[ds:r0+0x10], r1
                mov r0, 0x130
                mov word[ds:r0-0x10], r1
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r1 = 0x1234
                    },
                    RAMChecks = new()
                    {
                        [0x110] = 0x1234,
                        [0x120] = 0x1234
                    }
                }
            );
        }

        // TODO
        [TestMethod]
        public void MOV_dREGIMM16_dREGIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov word[ds:0x60], 0x1234
                mov r0, 0x40
                mov r1, 0x70
                mov word[ds:r0+0x10], word[ds:r1+0x10]
                mov word[ds:r0-0x10], word[ds:r1-0x10]
                ",
                new MachineState
                {
                    RAMChecks = new()
                    {
                        [0x30] = 0x1234,
                        [0x50] = 0x1234,
                        [0x60] = 0x1234,
                        [0x80] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void ADD_REG_REG()
        {
            AssertState(
                @"
                mov r0, 0x100
                mov r1, 0x200
                add r0, r1
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x300,
                        r1 = 0x200
                    }
                }
            );
        }

        [TestMethod]
        public void ADD_REG_IMM16()
        {
            AssertState(
                @"
                mov r0, 0x100
                add r0, 0x050
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x150
                    }
                }
            );
        }

        [TestMethod]
        public void ADD_REG_dREGIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x100
                mov r0, 0x50
                mov r1, 0x50
                add r0, word[ds:r1+0x30]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x150,
                        r1 = 0x030
                    },
                    RAMChecks = new Dictionary<ushort, short>
                    {
                        [0x80] = 0x100
                    }
                }
            );
        }

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