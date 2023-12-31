using Emulator;

namespace EmulatorTests
{
    [TestClass]
    public class InstructionTests
    {
        private readonly MicrocodeRom microcodeRom;

        public InstructionTests()
        {
            microcodeRom = Microcode.AssembleRom("Microcode.cmc");
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
                mov cs, 0x0000
                ",
                new CpuState
                {
                    r0 = 0x1234,
                    r1 = 0x4321,
                    r2 = 0x1234,
                    r3 = 0x4321,
                    sp = 0x1234,
                    ss = 0x4321,
                    cs = 0x0000,
                    ds = 0x0001,
                }
            );
        }

        [TestMethod]
        public void MOV_REG_dREGIMM16()
        {
            AssertState(
                @"
                mov [0x80], 0x1234
                mov [0x90], 0x1234
                mov r0, 0x80
                mov r1, [r0]
                mov r2, [r0+0x10]
                mov r0, 0xA0
                mov r3, [r0-0x10]
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
                mov [0x80], 0x1234
                mov r0, [0x80]
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
                mov [0x80], r0
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
                mov [0x80], 0x1234
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
                mov [0x110], 0x1234
                mov [0x80], [r0+0x10]
                mov r0, 0x120
                mov [0x90], [r0-0x10]
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
                mov [0x100], 0x1234
                mov [0x110], [0x100]
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
                mov [r0+0x10], r1
                mov r0, 0x130
                mov [r0-0x10], r1
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
                mov [0x80], 0x1234
                mov [0x60], 0x1234
                mov r0, 0x40
                mov r1, 0x70
                mov [r0+0x10], [r1+0x10]
                mov [r0-0x10], [r1-0x10]
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
                mov [0x80], 0x100
                mov r0, 0x50
                mov r1, 0x50
                add r0, [r1+0x30]
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
            var machine = new Machine(microcodeRom)
            {
                ShutdownWhenHalted = true,
                DebugOutput = true
            };

            var assembler = new Assembler(microcodeRom);
            var program = assembler.AssembleSource("nop\n" + source + "\nhlt");
            for (ushort i = 0; i < program.Length; ++i)
                machine.RAM.WriteByte(0, i, program[i]);

            machine.Run();

            var actualState = machine.CaptureState();
            if (!actualState.IsEqual(expectedState))
                Assert.Fail($"\nEXPECTED\n{expectedState}\n\nACTUAL\n{actualState}");
        }
    }
}