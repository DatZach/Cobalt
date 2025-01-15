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
        public void MOV_REG_IMM()
        {
            AssertState(
                @"
                mov r0, 0xFFFF
                mov r0, 0x12
                mov r1, 0x1234
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
                    r1 = 0x1234,
                    r2 = 0x1234,
                    r3 = 0x5600,
                    sp = 0x000F,
                    ss = 0x0080,
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
        public void MOV_sizeSEGREGplusIMM_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov r0, 0x90
                mov r1, 0x40
                mov word[ds:0x80], 0x1234
                mov word[ds:r1+0x10], word[ds:r0-0x10]
                mov word[ds:r1+0x20], byte[ds:r0-0x10]
                mov byte[ds:r1+0x30], byte[ds:r0-0x10]
                mov r0, 0x70
                mov word[ds:r1-0x10], word[ds:r0+0x10]
                mov word[ds:r1-0x20], byte[ds:r0+0x10]
                mov byte[ds:r1-0x30], byte[ds:r0+0x10]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0070,
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
                        [0x10] = 0x1200
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

        [TestMethod]
        public void MOV_sizeSEGREG_REG()
        {
            AssertState(
                @"
                mov r0, 0x80
                mov r1, 0x1234
                mov word[ds:r0], r1
                mov r0, 0x40
                mov byte[ds:r0], r1l
                mov r0, 0x41
                mov byte[ds:r0], r1h
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0041,
                        r1 = 0x1234
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x40] = 0x3412
                    }
                }
            );
        }

        [TestMethod]
        public void MOV_sizeSEGREG_IMM()
        {
            AssertState(
                @"
                mov r0, 0x80
                mov word[ds:r0], 0x1234
                mov r0, 0x50
                mov byte[ds:r0], 0x12
                mov r0, 0x51
                mov byte[ds:r0], 0x34
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0051
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x50] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void MOV_sizeSEGREG_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov r0, 0x80
                mov r1, 0x40
                mov r2, 0x90
                mov r3, 0xA0
                mov word[ds:0x50], 0x1234
                mov word[ds:0x60], 0x1234
                mov word[ds:r0], word[ds:r1+0x10]
                mov word[ds:r2], byte[ds:r1+0x20]
                mov byte[ds:r3], byte[ds:r1+0x20]
                mov r3, 0xA1
                mov r1, 0x52
                mov byte[ds:r3], byte[ds:r1-0x01]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0080,
                        r1 = 0x0052,
                        r2 = 0x0090,
                        r3 = 0x00A1
                    },
                    RAMChecks = new()
                    {
                        [0x50] = 0x1234,
                        [0x60] = 0x1234,
                        [0x80] = 0x1234,
                        [0x90] = 0x0012,
                        [0xA0] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void MOV_sizeSEGREG_sizeSEGREG()
        {
            AssertState(
                @"
                mov r0, 0x80
                mov r1, 0x40
                mov r2, 0x90
                mov r3, 0xA0
                mov word[ds:0x40], 0x1234
                mov word[ds:0x60], 0x1234
                mov word[ds:r0], word[ds:r1]
                mov word[ds:r2], byte[ds:r1]
                mov byte[ds:r3], byte[ds:r1]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0080,
                        r1 = 0x0040,
                        r2 = 0x0090,
                        r3 = 0x00A0
                    },
                    RAMChecks = new()
                    {
                        [0x40] = 0x1234,
                        [0x60] = 0x1234,
                        [0x80] = 0x1234,
                        [0x90] = 0x0012,
                        [0xA0] = 0x1200
                    }
                }
            );
        }

        [TestMethod]
        public void MOV_sizeSEGREG_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov r0, 0x80
                mov r1, 0x90
                mov r2, 0xA0
                mov word[ds:0x40], 0x1234
                mov word[ds:r0], word[ds:0x40]
                mov word[ds:r1], byte[ds:0x40]
                mov byte[ds:r2], byte[ds:0x40]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0080,
                        r1 = 0x0090,
                        r2 = 0x00A0
                    },
                    RAMChecks = new()
                    {
                        [0x40] = 0x1234,
                        [0x80] = 0x1234,
                        [0x90] = 0x0012,
                        [0xA0] = 0x1200
                    }
                }
            );
        }

        [TestMethod]
        public void MOV_sizeSEGuIMM16_REG()
        {
            AssertState(
                @"
                mov r0, 0x1234
                mov word[ds:0x40], r0
                mov word[ds:0x50], r0h
                mov byte[ds:0x60], r0l
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234
                    },
                    RAMChecks = new()
                    {
                        [0x40] = 0x1234,
                        [0x50] = 0x0012,
                        [0x60] = 0x3400
                    }
                }
            );
        }

        [TestMethod]
        public void MOV_sizeSEGuIMM16_IMM()
        {
            AssertState(
                @"
                mov word[ds:0x40], 0x1234
                mov word[ds:0x50], 0x12
                mov byte[ds:0x60], 0x34
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        
                    },
                    RAMChecks = new()
                    {
                        [0x40] = 0x1234,
                        [0x50] = 0x0012,
                        [0x60] = 0x3400
                    }
                }
            );
        }

        [TestMethod]
        public void MOV_sizeSEGuIMM16_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov r0, 0x80
                mov r1, 0x90
                mov word[ds:r1], 0x1234
                mov word[ds:0x40], word[ds:r0+0x10]
                mov word[ds:0x50], byte[ds:r0+0x10]
                mov byte[ds:0x60], byte[ds:r0+0x10]
                mov r0, 0xA0
                mov byte[ds:0x61], byte[ds:r0-0x0F]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x00A0,
                        r1 = 0x0090
                    },
                    RAMChecks = new()
                    {
                        [0x90] = 0x1234,
                        [0x40] = 0x1234,
                        [0x50] = 0x0012,
                        [0x60] = 0x1234,
                    }
                }
            );
        }

        [TestMethod]
        public void MOV_sizeSEGuIMM16_sizeSEGREG()
        {
            AssertState(
                @"
                mov r0, 0x80
                mov word[ds:0x80], 0x1234
                mov word[ds:0x40], word[ds:r0]
                mov word[ds:0x50], byte[ds:r0]
                mov byte[ds:0x60], byte[ds:r0]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0080
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x40] = 0x1234,
                        [0x50] = 0x0012,
                        [0x60] = 0x1200,
                    }
                }
            );
        }

        [TestMethod]
        public void MOV_sizeSEGuIMM16_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov word[ds:0x40], word[ds:0x80]
                mov word[ds:0x50], byte[ds:0x80]
                mov byte[ds:0x60], byte[ds:0x80]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x40] = 0x1234,
                        [0x50] = 0x0012,
                        [0x60] = 0x1200,
                    }
                }
            );
        }

        [TestMethod]
        public void ADD_REG_REG()
        {
            AssertState(
                @"
                mov r0, 0x1234
                mov r1, 0x4321
                add r0, r1
                ",
                new CpuState
                {
                    r0 = 0x5555,
                    r1 = 0x4321
                }
            );
        }

        [TestMethod]
        public void ADD_REG_IMM()
        {
            AssertState(
                @"
                mov r1, 0x1234
                add r1, 0x21
                mov r0, 0x1234
                add r0, 0x4321
                ",
                new CpuState
                {
                    r0 = 0x5555,
                    r1 = 0x1255
                }
            );
        }

        [TestMethod]
        public void ADD_REG_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov r0, 0x1234
                mov r1, 0x1234
                mov r2, 0x1234
                mov r3, 0x70
                add r0, word[ds:r3+0x10]
                add r1, byte[ds:r3+0x10]
                mov r3, 0x90
                add r2, word[ds:r3-0x10]
                ",
                new CpuState
                {
                    r0 = 0x5555,
                    r1 = 0x1277,
                    r2 = 0x5555,
                    r3 = 0x0090
                }
            );
        }

        [TestMethod]
        public void ADD_REG_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov r0, 0x1234
                mov r1, 0x1234
                mov r3, 0x80
                add r0, word[ds:r3]
                add r1, byte[ds:r3]
                ",
                new CpuState
                {
                    r0 = 0x5555,
                    r1 = 0x1277,
                    r3 = 0x0080
                }
            );
        }

        [TestMethod]
        public void ADD_REG_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov r0, 0x1234
                mov r1, 0x1234
                add r0, word[ds:0x80]
                add r1, byte[ds:0x80]
                ",
                new CpuState
                {
                    r0 = 0x5555,
                    r1 = 0x1277
                }
            );
        }

        [TestMethod]
        public void ADD_sizeSEGREGplusIMM_REG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r0, 0x1234
                mov r2, 0xA0
                mov r3, 0x70
                add word[ds:r3+0x10], r0
                add byte[ds:r2-0x0F], r0l
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234,
                        r2 = 0x00A0,
                        r3 = 0x0070
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x5555,
                        [0x90] = 0x4355
                    }
                }
            );
        }

        [TestMethod]
        public void ADD_sizeSEGREGplusIMM_IMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r2, 0xA0
                mov r3, 0x70
                add word[ds:r3+0x10], 0x1234
                add byte[ds:r2-0x0F], 0x34
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r2 = 0x00A0,
                        r3 = 0x0070
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x5555,
                        [0x90] = 0x4355
                    }
                }
            );
        }

        [TestMethod]
        public void ADD_sizeSEGREGplusIMM_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r2, 0xA0
                mov r3, 0x70
                add word[ds:r3+0x10], word[ds:r3-0x30]
                add byte[ds:r2-0x0F], byte[ds:r3-0x2F]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r2 = 0x00A0,
                        r3 = 0x0070
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x5555,
                        [0x90] = 0x4355,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void ADD_sizeSEGREGplusIMM_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r1, 0x40
                mov r2, 0xA0
                mov r3, 0x70
                add word[ds:r3+0x10], word[ds:r1]
                mov r1, 0x41
                add byte[ds:r2-0x0F], byte[ds:r1]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r1 = 0x0041,
                        r2 = 0x00A0,
                        r3 = 0x0070
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x5555,
                        [0x90] = 0x4355,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void ADD_sizeSEGREGplusIMM_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r2, 0xA0
                mov r3, 0x70
                add word[ds:r3+0x10], word[ds:0x40]
                add byte[ds:r2-0x0F], byte[ds:0x41]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r2 = 0x00A0,
                        r3 = 0x0070
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x5555,
                        [0x90] = 0x4355,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void ADD_sizeSEGREG_REG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r0, 0x1234
                mov r2, 0x80
                mov r3, 0x91
                add word[ds:r2], r0
                add byte[ds:r3], r0l
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234,
                        r2 = 0x0080,
                        r3 = 0x0091
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x5555,
                        [0x90] = 0x4355
                    }
                }
            );
        }

        [TestMethod]
        public void ADD_sizeSEGREG_IMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r2, 0x80
                mov r3, 0x91
                add word[ds:r2], 0x1234
                add byte[ds:r3], 0x34
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r2 = 0x0080,
                        r3 = 0x0091
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x5555,
                        [0x90] = 0x4355
                    }
                }
            );
        }

        [TestMethod]
        public void ADD_sizeSEGREG_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r0, 0x30
                mov r1, 0x50
                mov r2, 0x80
                mov r3, 0x91
                add word[ds:r2], word[ds:r0+0x10]
                add byte[ds:r3], byte[ds:r1-0x0F]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0030,
                        r1 = 0x0050,
                        r2 = 0x0080,
                        r3 = 0x0091
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x5555,
                        [0x90] = 0x4355
                    }
                }
            );
        }

        [TestMethod]
        public void ADD_sizeSEGREG_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r0, 0x40
                mov r1, 0x41
                mov r2, 0x80
                mov r3, 0x91
                add word[ds:r2], word[ds:r0]
                add byte[ds:r3], byte[ds:r1]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0040,
                        r1 = 0x0041,
                        r2 = 0x0080,
                        r3 = 0x0091
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x5555,
                        [0x90] = 0x4355
                    }
                }
            );
        }

        [TestMethod]
        public void ADD_sizeSEGREG_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r2, 0x80
                mov r3, 0x91
                add word[ds:r2], word[ds:0x40]
                add byte[ds:r3], byte[ds:0x41]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r2 = 0x0080,
                        r3 = 0x0091
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x5555,
                        [0x90] = 0x4355
                    }
                }
            );
        }

        [TestMethod]
        public void ADD_sizeSEGuIMM16_REG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r0, 0x1234
                add word[ds:0x80], r0
                add byte[ds:0x91], r0l
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x5555,
                        [0x90] = 0x4355
                    }
                }
            );
        }

        [TestMethod]
        public void ADD_sizeSEGuIMM16_IMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                add word[ds:0x80], 0x1234
                add byte[ds:0x91], 0x34
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {

                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x5555,
                        [0x90] = 0x4355
                    }
                }
            );
        }

        [TestMethod]
        public void ADD_sizeSEGuIMM16_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r1, 0x30
                mov r2, 0x50
                add word[ds:0x80], word[ds:r1+0x10]
                add byte[ds:0x91], byte[ds:r2-0x0F]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r1 = 0x0030,
                        r2 = 0x0050
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x5555,
                        [0x90] = 0x4355,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void ADD_sizeSEGuIMM16_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r1, 0x40
                mov r2, 0x41
                add word[ds:0x80], word[ds:r1]
                add byte[ds:0x91], byte[ds:r2]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r1 = 0x0040,
                        r2 = 0x0041
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x5555,
                        [0x90] = 0x4355,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void ADD_sizeSEGuIMM16_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                add word[ds:0x80], word[ds:0x40]
                add byte[ds:0x91], byte[ds:0x41]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {

                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x5555,
                        [0x90] = 0x4355,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void BCMP_sizeSEGREG_IMM()
        {
            AssertState(
                @"
                mov r2, data
            a:  bcmp byte[cs:r2], 0
                jnz a
                sub r2, data
                hlt

                data: db ""Hello, World!^0""
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r2 = 14
                    },
                    RAMChecks = new()
                    {
                        
                    }
                }
            );
        }

        [TestMethod]
        public void BMOV_sizeSEGREG_sizeSEGREG()
        {
            AssertState(
                @"
                mov r2, data
                mov r0, 0x40
                mov r1, 8
            a:  bmov byte[ds:r0], byte[cs:r2]
                lnz r1, a
                sub r2, data
                hlt

                data: db ""^x12^x34^x56^x78^x9A^xBC^xDE^xF0""
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x48,
                        r1 = 0x00,
                        r2 = 0x08
                    },
                    RAMChecks = new()
                    {
                        [0x40] = 0x1234,
                        [0x42] = 0x5678,
                        [0x44] = 0x9ABC,
                        [0x46] = 0xDEF0,
                    }
                }
            );
        }

        [TestMethod]
        public void LNZ_REG_IMM16()
        {
            AssertState(
                @"
                mov r3, 10
                mov r0, 32
                loop:
                    add r0, 1
                    lnz r3, loop
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 42,
                        r3 = 0
                    },
                    RAMChecks = new()
                    {
                        
                    }
                }
            );
        }

        private void AssertState(string source, MachineState expectedState)
        {
            var devices = Array.Empty<DeviceConfigBase>();
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
                Assert.Fail($"\nEXPECTED\n{expectedState}\n\nACTUAL\n{actualState}\n{bootRom.ToString(0, 64)}");
        }
    }
}