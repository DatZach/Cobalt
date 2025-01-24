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
                add r1, byte[ds:r3+0x11]
                mov r3, 0x90
                add r2, word[ds:r3-0x10]
                ",
                new CpuState
                {
                    r0 = 0x5555,
                    r1 = 0x1255,
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
                mov r3, 0x81
                add r1, byte[ds:r3]
                ",
                new CpuState
                {
                    r0 = 0x5555,
                    r1 = 0x1255,
                    r3 = 0x0081
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
                add r1, byte[ds:0x81]
                ",
                new CpuState
                {
                    r0 = 0x5555,
                    r1 = 0x1255
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
        public void SUB_REG_REG()
        {
            AssertState(
                @"
                mov r0, 0x4321
                mov r1, 0x1234
                sub r0, r1
                ",
                new CpuState
                {
                    r0 = 0x30ED,
                    r1 = 0x1234
                }
            );
        }

        [TestMethod]
        public void SUB_REG_IMM()
        {
            AssertState(
                @"
                mov r1, 0x1234
                sub r1, 0x21
                mov r0, 0x1234
                sub r0, 0x4321
                ",
                new CpuState
                {
                    r0 = 0xCF13,
                    r1 = 0x1213
                }
            );
        }

        [TestMethod]
        public void SUB_REG_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov r0, 0x1234
                mov r1, 0x1234
                mov r2, 0x1234
                mov r3, 0x70
                sub r0, word[ds:r3+0x10]
                sub r1, byte[ds:r3+0x10]
                mov r3, 0x90
                sub r2, word[ds:r3-0x10]
                ",
                new CpuState
                {
                    r0 = 0xCF13,
                    r1 = 0x11F1,
                    r2 = 0xCF13,
                    r3 = 0x0090
                }
            );
        }

        [TestMethod]
        public void SUB_REG_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov r0, 0x1234
                mov r1, 0x1234
                mov r3, 0x80
                sub r0, word[ds:r3]
                sub r1, byte[ds:r3]
                ",
                new CpuState
                {
                    r0 = 0xCF13,
                    r1 = 0x11F1,
                    r3 = 0x0080
                }
            );
        }

        [TestMethod]
        public void SUB_REG_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov r0, 0x1234
                mov r1, 0x1234
                sub r0, word[ds:0x80]
                sub r1, byte[ds:0x80]
                ",
                new CpuState
                {
                    r0 = 0xCF13,
                    r1 = 0x11F1
                }
            );
        }

        [TestMethod]
        public void SUB_sizeSEGREGplusIMM_REG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r0, 0x1234
                mov r2, 0xA0
                mov r3, 0x70
                sub word[ds:r3+0x10], r0
                sub byte[ds:r2-0x0F], r0l
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
                        [0x80] = 0x30ED,
                        [0x90] = 0x43ED
                    }
                }
            );
        }

        [TestMethod]
        public void SUB_sizeSEGREGplusIMM_IMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r2, 0xA0
                mov r3, 0x70
                sub word[ds:r3+0x10], 0x1234
                sub byte[ds:r2-0x0F], 0x34
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
                        [0x80] = 0x30ED,
                        [0x90] = 0x43ED
                    }
                }
            );
        }

        [TestMethod]
        public void SUB_sizeSEGREGplusIMM_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r2, 0xA0
                mov r3, 0x70
                sub word[ds:r3+0x10], word[ds:r3-0x30]
                sub byte[ds:r2-0x0F], byte[ds:r3-0x2F]
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
                        [0x80] = 0x30ED,
                        [0x90] = 0x43ED,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void SUB_sizeSEGREGplusIMM_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r1, 0x40
                mov r2, 0xA0
                mov r3, 0x70
                sub word[ds:r3+0x10], word[ds:r1]
                mov r1, 0x41
                sub byte[ds:r2-0x0F], byte[ds:r1]
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
                        [0x80] = 0x30ED,
                        [0x90] = 0x43ED,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void SUB_sizeSEGREGplusIMM_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r2, 0xA0
                mov r3, 0x70
                sub word[ds:r3+0x10], word[ds:0x40]
                sub byte[ds:r2-0x0F], byte[ds:0x41]
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
                        [0x80] = 0x30ED,
                        [0x90] = 0x43ED,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void SUB_sizeSEGREG_REG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r0, 0x1234
                mov r2, 0x80
                mov r3, 0x91
                sub word[ds:r2], r0
                sub byte[ds:r3], r0l
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
                        [0x80] = 0x30ED,
                        [0x90] = 0x43ED
                    }
                }
            );
        }

        [TestMethod]
        public void SUB_sizeSEGREG_IMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r2, 0x80
                mov r3, 0x91
                sub word[ds:r2], 0x1234
                sub byte[ds:r3], 0x34
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
                        [0x80] = 0x30ED,
                        [0x90] = 0x43ED
                    }
                }
            );
        }

        [TestMethod]
        public void SUB_sizeSEGREG_sizeSEGREGplusIMM()
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
                sub word[ds:r2], word[ds:r0+0x10]
                sub byte[ds:r3], byte[ds:r1-0x0F]
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
                        [0x80] = 0x30ED,
                        [0x90] = 0x43ED
                    }
                }
            );
        }

        [TestMethod]
        public void SUB_sizeSEGREG_sizeSEGREG()
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
                sub word[ds:r2], word[ds:r0]
                sub byte[ds:r3], byte[ds:r1]
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
                        [0x80] = 0x30ED,
                        [0x90] = 0x43ED
                    }
                }
            );
        }

        [TestMethod]
        public void SUB_sizeSEGREG_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r2, 0x80
                mov r3, 0x91
                sub word[ds:r2], word[ds:0x40]
                sub byte[ds:r3], byte[ds:0x41]
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
                        [0x80] = 0x30ED,
                        [0x90] = 0x43ED
                    }
                }
            );
        }

        [TestMethod]
        public void SUB_sizeSEGuIMM16_REG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r0, 0x1234
                sub word[ds:0x80], r0
                sub byte[ds:0x91], r0l
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x30ED,
                        [0x90] = 0x43ED
                    }
                }
            );
        }

        [TestMethod]
        public void SUB_sizeSEGuIMM16_IMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                sub word[ds:0x80], 0x1234
                sub byte[ds:0x91], 0x34
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {

                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x30ED,
                        [0x90] = 0x43ED
                    }
                }
            );
        }

        [TestMethod]
        public void SUB_sizeSEGuIMM16_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r1, 0x30
                mov r2, 0x50
                sub word[ds:0x80], word[ds:r1+0x10]
                sub byte[ds:0x91], byte[ds:r2-0x0F]
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
                        [0x80] = 0x30ED,
                        [0x90] = 0x43ED,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void SUB_sizeSEGuIMM16_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r1, 0x40
                mov r2, 0x41
                sub word[ds:0x80], word[ds:r1]
                sub byte[ds:0x91], byte[ds:r2]
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
                        [0x80] = 0x30ED,
                        [0x90] = 0x43ED,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void SUB_sizeSEGuIMM16_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                sub word[ds:0x80], word[ds:0x40]
                sub byte[ds:0x91], byte[ds:0x41]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {

                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x30ED,
                        [0x90] = 0x43ED,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void MUL_REG_IMM()
        {
            AssertState(
                @"
                mov r0, 7
                mul r0, 9
                mov r1, 0
                mul r1, 9
                mov r2, 7
                mov r2, 0
                ",
                new CpuState
                {
                    r0 = 63,
                    r1 = 0,
                    r2 = 0
                }
            );
        }

        [TestMethod]
        public void MUL_REG_REG()
        {
            AssertState(
                @"
                mov r0, 7
                mov r1, 9
                mul r0, r1
                mov r2, 0
                mov r3, 9
                mul r2, r3
                mov ss, 7
                mov sp, 0
                mul ss, sp
                ",
                new CpuState
                {
                    r0 = 63,
                    r1 = 9,
                    r2 = 0,
                    r3 = 9,
                    ss = 0,
                    sp = 0
                }
            );
        }

        [TestMethod]
        public void MUL_REG_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r0, 7
                mov r1, 0
                mov r2, 0x4321
                mov r3, 0x70
                mul r0, word[ds:r3+0x10]
                mul r1, byte[ds:r3+0x10]
                mov r3, 0x90
                mul r2, word[ds:r3-0x10]
                ",
                new CpuState
                {
                    r0 = 0x7F6C,
                    r1 = 0x0000,
                    r2 = 0xF4B4,
                    r3 = 0x0090
                }
            );
        }

        [TestMethod]
        public void MUL_REG_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r0, 7
                mov r1, 0
                mov r2, 1
                mov r3, 0x80
                mul r0, word[ds:r3]
                mul r1, byte[ds:r3]
                mul r2, byte[ds:r3]
                ",
                new CpuState
                {
                    r0 = 0x7F6C,
                    r1 = 0x0000,
                    r2 = 0x0012,
                    r3 = 0x0080
                }
            );
        }

        [TestMethod]
        public void MUL_REG_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r0, 7
                mov r1, 0
                mov r2, 1
                mul r0, word[ds:0x80]
                mul r1, byte[ds:0x80]
                mul r2, byte[ds:0x80]
                ",
                new CpuState
                {
                    r0 = 0x7F6C,
                    r1 = 0x0000,
                    r2 = 0x0012
                }
            );
        }
        
        [TestMethod]
        public void DIV_REG_REG()
        {
            AssertState(
                @"
                mov r0, 10
                mov r1, 2
                div r0, r1
                ",
                new CpuState
                {
                    r0 = 5,
                    r1 = 0
                }
            );

            AssertState(
                @"
                mov r0, 5
                mov r1, 2
                div r0, r1
                ",
                new CpuState
                {
                    r0 = 2,
                    r1 = 1
                }
            );

            AssertState(
                @"
                mov r3, 0xFFFF
                mov [0x0000:0x0000], ExceptionHandler
                sie 1

                mov r0, 10
                mov r1, 0
                div r0, r1
                hlt

                ExceptionHandler:
                    pop r3
                    hlt
                ",
                new CpuState
                {
                    r0 = 10,
                    r1 = 0,
                    r3 = 0x0104     // DIV_BY_0 ZF cf sf
                }
            );
        }

        [TestMethod]
        public void DIV_REG_IMM()
        {
            AssertState(
                @"
                mov r0, 10
                div r0, 2
                ",
                new CpuState
                {
                    r0 = 5
                }
            );

            AssertState(
                @"
                mov r0, 5
                div r0, 2
                ",
                new CpuState
                {
                    r0 = 2
                }
            );

            AssertState(
                @"
                mov r3, 0xFFFF
                mov [0x0000:0x0000], ExceptionHandler
                sie 1

                mov r0, 10
                div r0, 0
                hlt

                ExceptionHandler:
                    pop r3
                    hlt
                ",
                new CpuState
                {
                    r0 = 10,
                    r3 = 0x0104     // DIV_BY_0 ZF cf sf
                }
            );
        }

        [TestMethod]
        public void DIV_REG_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 2
                mov r0, 10
                mov r3, 0x80
                div r0, word[ds:r3]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 5,
                        r3 = 0x80
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 2
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 2
                mov r0, 5
                mov r3, 0x80
                div r0, word[ds:r3]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 2,
                        r3 = 0x80
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 2
                    }
                }
            );

            AssertState(
                @"
                mov r3, 0xFFFF
                mov [0x0000:0x0000], ExceptionHandler
                sie 1

                mov word[ds:0x80], 0
                mov r0, 10
                mov r2, 0x80
                div r0, word[ds:r2]
                hlt

                ExceptionHandler:
                    pop r3
                    hlt
                ",
                new CpuState
                {
                    r0 = 10,
                    r2 = 0x80,
                    r3 = 0x0104     // DIV_BY_0 ZF cf sf
                }
            );
        }

        [TestMethod]
        public void DIV_REG_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 2
                mov r0, 10
                div r0, word[ds:0x80]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 5
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 2
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 2
                mov r0, 5
                div r0, word[ds:0x80]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 2
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 2
                    }
                }
            );

            AssertState(
                @"
                mov r3, 0xFFFF
                mov [0x0000:0x0000], ExceptionHandler
                sie 1

                mov word[ds:0x80], 0
                mov r0, 10
                div r0, word[ds:0x80]
                hlt

                ExceptionHandler:
                    pop r3
                    hlt
                ",
                new CpuState
                {
                    r0 = 10,
                    r3 = 0x0104     // DIV_BY_0 ZF cf sf
                }
            );
        }

        [TestMethod]
        public void SHL_REG_REG()
        {
            AssertState(
                @"
                mov r0, 0x4321
                mov r1, 3
                shl r0, r1
                ",
                new CpuState
                {
                    r0 = 0x1908,
                    r1 = 3
                }
            );
        }

        [TestMethod]
        public void SHL_REG_IMM()
        {
            AssertState(
                @"
                mov r0, 0x4321
                shl r0, 3
                ",
                new CpuState
                {
                    r0 = 0x1908
                }
            );
        }

        [TestMethod]
        public void SHL_REG_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x0303
                mov r0, 0x4321
                mov r1, 0x4321
                mov r2, 0x4321
                mov r3, 0x70
                shl r0, word[ds:r3+0x10]
                shl r1, byte[ds:r3+0x10]
                mov r3, 0x90
                shl r2, word[ds:r3-0x10]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1908,
                        r1 = 0x1908,
                        r2 = 0x1908,
                        r3 = 0x0090
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x0303
                    }
                }
            );
        }

        [TestMethod]
        public void SHL_REG_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x0303
                mov r0, 0x4321
                mov r1, 0x4321
                mov r3, 0x80
                shl r0, word[ds:r3]
                shl r1, byte[ds:r3]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1908,
                        r1 = 0x1908,
                        r3 = 0x0080
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x0303
                    }
                }
            );
        }

        [TestMethod]
        public void SHL_REG_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x0303
                mov r0, 0x4321
                mov r1, 0x4321
                mov r2, 0x4321
                shl r0, word[ds:0x80]
                shl r1, byte[ds:0x80]
                shl r2l, byte[ds:0x80]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1908,
                        r1 = 0x1908,
                        r2 = 0x4308
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x0303
                    }
                }
            );
        }

        [TestMethod]
        public void SHL_sizeSEGREGplusIMM_REG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r0, 3
                mov r2, 0xA0
                mov r3, 0x70
                shl word[ds:r3+0x10], r0
                shl byte[ds:r2-0x0F], r0l
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 3,
                        r2 = 0x00A0,
                        r3 = 0x0070
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1908,
                        [0x90] = 0x4308
                    }
                }
            );
        }

        [TestMethod]
        public void SHL_sizeSEGREGplusIMM_IMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r2, 0xA0
                mov r3, 0x70
                shl word[ds:r3+0x10], 3
                shl byte[ds:r2-0x0F], 3
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
                        [0x80] = 0x1908,
                        [0x90] = 0x4308
                    }
                }
            );
        }

        [TestMethod]
        public void SHL_sizeSEGREGplusIMM_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 3
                mov r2, 0xA0
                mov r3, 0x70
                shl word[ds:r3+0x10], word[ds:r3-0x30]
                shl byte[ds:r2-0x0F], byte[ds:r3-0x2F]
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
                        [0x80] = 0x1908,
                        [0x90] = 0x4308,
                        [0x40] = 3
                    }
                }
            );
        }

        [TestMethod]
        public void SHL_sizeSEGREGplusIMM_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 3
                mov r1, 0x40
                mov r2, 0xA0
                mov r3, 0x70
                shl word[ds:r3+0x10], word[ds:r1]
                mov r1, 0x41
                shl byte[ds:r2-0x0F], byte[ds:r1]
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
                        [0x80] = 0x1908,
                        [0x90] = 0x4308,
                        [0x40] = 3
                    }
                }
            );
        }

        [TestMethod]
        public void SHL_sizeSEGREGplusIMM_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 3
                mov r2, 0xA0
                mov r3, 0x70
                shl word[ds:r3+0x10], word[ds:0x40]
                shl byte[ds:r2-0x0F], byte[ds:0x41]
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
                        [0x80] = 0x1908,
                        [0x90] = 0x4308,
                        [0x40] = 3
                    }
                }
            );
        }

        [TestMethod]
        public void SHL_sizeSEGREG_REG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r0, 3
                mov r2, 0x80
                mov r3, 0x90
                shl word[ds:r2], r0
                shl byte[ds:r3], r0l
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 3,
                        r2 = 0x0080,
                        r3 = 0x0090
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1908,
                        [0x90] = 0x1821
                    }
                }
            );
        }

        [TestMethod]
        public void SHL_sizeSEGREG_IMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r2, 0x80
                mov r3, 0x90
                shl word[ds:r2], 3
                shl byte[ds:r3], 3
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r2 = 0x0080,
                        r3 = 0x0090
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1908,
                        [0x90] = 0x1821
                    }
                }
            );
        }

        [TestMethod]
        public void SHL_sizeSEGREG_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 3
                mov r0, 0x30
                mov r1, 0x50
                mov r2, 0x80
                mov r3, 0x91
                shl word[ds:r2], word[ds:r0+0x10]
                shl byte[ds:r3], byte[ds:r1-0x0F]
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
                        [0x80] = 0x1908,
                        [0x90] = 0x4308
                    }
                }
            );
        }

        [TestMethod]
        public void SHL_sizeSEGREG_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 3
                mov r0, 0x40
                mov r1, 0x41
                mov r2, 0x80
                mov r3, 0x90
                shl word[ds:r2], word[ds:r0]
                shl byte[ds:r3], byte[ds:r1]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0040,
                        r1 = 0x0041,
                        r2 = 0x0080,
                        r3 = 0x0090
                    },
                    RAMChecks = new()
                    {
                        [0x40] = 0x0003,
                        [0x80] = 0x1908,
                        [0x90] = 0x1821
                    }
                }
            );
        }

        [TestMethod]
        public void SHL_sizeSEGREG_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 3
                mov r2, 0x80
                mov r3, 0x90
                shl word[ds:r2], word[ds:0x40]
                shl byte[ds:r3], byte[ds:0x41]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r2 = 0x0080,
                        r3 = 0x0090
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1908,
                        [0x90] = 0x1821
                    }
                }
            );
        }

        [TestMethod]
        public void SHL_sizeSEGuIMM16_REG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r0, 3
                shl word[ds:0x80], r0
                shl byte[ds:0x90], r0l
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 3
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1908,
                        [0x90] = 0x1821
                    }
                }
            );
        }

        [TestMethod]
        public void SHL_sizeSEGuIMM16_IMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                shl word[ds:0x80], 3
                shl byte[ds:0x90], 3
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {

                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1908,
                        [0x90] = 0x1821
                    }
                }
            );
        }

        [TestMethod]
        public void SHL_sizeSEGuIMM16_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 3
                mov r1, 0x30
                mov r2, 0x50
                shl word[ds:0x80], word[ds:r1+0x10]
                shl byte[ds:0x90], byte[ds:r2-0x0F]
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
                        [0x80] = 0x1908,
                        [0x90] = 0x1821,
                        [0x40] = 3
                    }
                }
            );
        }

        [TestMethod]
        public void SHL_sizeSEGuIMM16_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 3
                mov r1, 0x40
                mov r2, 0x41
                shl word[ds:0x80], word[ds:r1]
                shl byte[ds:0x90], byte[ds:r2]
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
                        [0x80] = 0x1908,
                        [0x90] = 0x1821,
                        [0x40] = 3
                    }
                }
            );
        }

        [TestMethod]
        public void SHL_sizeSEGuIMM16_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 3
                shl word[ds:0x80], word[ds:0x40]
                shl byte[ds:0x90], byte[ds:0x41]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {

                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1908,
                        [0x90] = 0x1821,
                        [0x40] = 3
                    }
                }
            );
        }

        [TestMethod]
        public void SHR_REG_REG()
        {
            AssertState(
                @"
                mov r0, 0x4321
                mov r1, 3
                shr r0, r1
                ",
                new CpuState
                {
                    r0 = 0x0864,
                    r1 = 3
                }
            );
        }

        [TestMethod]
        public void SHR_REG_IMM()
        {
            AssertState(
                @"
                mov r0, 0x4321
                shr r0, 3
                ",
                new CpuState
                {
                    r0 = 0x0864
                }
            );
        }

        [TestMethod]
        public void SHR_REG_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x0303
                mov r0, 0x4321
                mov r1, 0x4321
                mov r2, 0x4321
                mov r3, 0x70
                shr r0, word[ds:r3+0x10]
                shr r1, byte[ds:r3+0x10]
                mov r3, 0x90
                shr r2, word[ds:r3-0x10]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0864,
                        r1 = 0x0864,
                        r2 = 0x0864,
                        r3 = 0x0090
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x0303
                    }
                }
            );
        }

        [TestMethod]
        public void SHR_REG_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x0303
                mov r0, 0x4321
                mov r1, 0x4321
                mov r3, 0x80
                shr r0, word[ds:r3]
                shr r1, byte[ds:r3]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0864,
                        r1 = 0x0864,
                        r3 = 0x0080
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x0303
                    }
                }
            );
        }

        [TestMethod]
        public void SHR_REG_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x0303
                mov r0, 0x4321
                mov r1, 0x4321
                mov r2, 0x4321
                shr r0, word[ds:0x80]
                shr r1, byte[ds:0x80]
                shr r2l, byte[ds:0x80]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0864,
                        r1 = 0x0864,
                        r2 = 0x4304
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x0303
                    }
                }
            );
        }

        [TestMethod]
        public void SHR_sizeSEGREGplusIMM_REG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r0, 3
                mov r2, 0xA0
                mov r3, 0x70
                shr word[ds:r3+0x10], r0
                shr byte[ds:r2-0x0F], r0l
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 3,
                        r2 = 0x00A0,
                        r3 = 0x0070
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x0864,
                        [0x90] = 0x4304
                    }
                }
            );
        }

        [TestMethod]
        public void SHR_sizeSEGREGplusIMM_IMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r2, 0xA0
                mov r3, 0x70
                shr word[ds:r3+0x10], 3
                shr byte[ds:r2-0x0F], 3
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
                        [0x80] = 0x0864,
                        [0x90] = 0x4304
                    }
                }
            );
        }

        [TestMethod]
        public void SHR_sizeSEGREGplusIMM_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 3
                mov r2, 0xA0
                mov r3, 0x70
                shr word[ds:r3+0x10], word[ds:r3-0x30]
                shr byte[ds:r2-0x0F], byte[ds:r3-0x2F]
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
                        [0x80] = 0x0864,
                        [0x90] = 0x4304,
                        [0x40] = 3
                    }
                }
            );
        }

        [TestMethod]
        public void SHR_sizeSEGREGplusIMM_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 3
                mov r1, 0x40
                mov r2, 0xA0
                mov r3, 0x70
                shr word[ds:r3+0x10], word[ds:r1]
                mov r1, 0x41
                shr byte[ds:r2-0x0F], byte[ds:r1]
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
                        [0x80] = 0x0864,
                        [0x90] = 0x4304,
                        [0x40] = 3
                    }
                }
            );
        }

        [TestMethod]
        public void SHR_sizeSEGREGplusIMM_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 3
                mov r2, 0xA0
                mov r3, 0x70
                shr word[ds:r3+0x10], word[ds:0x40]
                shr byte[ds:r2-0x0F], byte[ds:0x41]
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
                        [0x80] = 0x0864,
                        [0x90] = 0x4304,
                        [0x40] = 3
                    }
                }
            );
        }

        [TestMethod]
        public void SHR_sizeSEGREG_REG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r0, 3
                mov r2, 0x80
                mov r3, 0x90
                shr word[ds:r2], r0
                shr byte[ds:r3], r0l
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 3,
                        r2 = 0x0080,
                        r3 = 0x0090
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x0864,
                        [0x90] = 0x0821
                    }
                }
            );
        }

        [TestMethod]
        public void SHR_sizeSEGREG_IMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r2, 0x80
                mov r3, 0x90
                shr word[ds:r2], 3
                shr byte[ds:r3], 3
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r2 = 0x0080,
                        r3 = 0x0090
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x0864,
                        [0x90] = 0x0821
                    }
                }
            );
        }

        [TestMethod]
        public void SHR_sizeSEGREG_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 3
                mov r0, 0x30
                mov r1, 0x50
                mov r2, 0x80
                mov r3, 0x91
                shr word[ds:r2], word[ds:r0+0x10]
                shr byte[ds:r3], byte[ds:r1-0x0F]
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
                        [0x80] = 0x0864,
                        [0x90] = 0x4304
                    }
                }
            );
        }

        [TestMethod]
        public void SHR_sizeSEGREG_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 3
                mov r0, 0x40
                mov r1, 0x41
                mov r2, 0x80
                mov r3, 0x90
                shr word[ds:r2], word[ds:r0]
                shr byte[ds:r3], byte[ds:r1]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0040,
                        r1 = 0x0041,
                        r2 = 0x0080,
                        r3 = 0x0090
                    },
                    RAMChecks = new()
                    {
                        [0x40] = 0x0003,
                        [0x80] = 0x0864,
                        [0x90] = 0x0821
                    }
                }
            );
        }

        [TestMethod]
        public void SHR_sizeSEGREG_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 3
                mov r2, 0x80
                mov r3, 0x90
                shr word[ds:r2], word[ds:0x40]
                shr byte[ds:r3], byte[ds:0x41]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r2 = 0x0080,
                        r3 = 0x0090
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x0864,
                        [0x90] = 0x0821
                    }
                }
            );
        }

        [TestMethod]
        public void SHR_sizeSEGuIMM16_REG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r0, 3
                shr word[ds:0x80], r0
                shr byte[ds:0x90], r0l
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 3
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x0864,
                        [0x90] = 0x0821
                    }
                }
            );
        }

        [TestMethod]
        public void SHR_sizeSEGuIMM16_IMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                shr word[ds:0x80], 3
                shr byte[ds:0x90], 3
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {

                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x0864,
                        [0x90] = 0x0821
                    }
                }
            );
        }

        [TestMethod]
        public void SHR_sizeSEGuIMM16_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 3
                mov r1, 0x30
                mov r2, 0x50
                shr word[ds:0x80], word[ds:r1+0x10]
                shr byte[ds:0x90], byte[ds:r2-0x0F]
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
                        [0x80] = 0x0864,
                        [0x90] = 0x0821,
                        [0x40] = 3
                    }
                }
            );
        }

        [TestMethod]
        public void SHR_sizeSEGuIMM16_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 3
                mov r1, 0x40
                mov r2, 0x41
                shr word[ds:0x80], word[ds:r1]
                shr byte[ds:0x90], byte[ds:r2]
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
                        [0x80] = 0x0864,
                        [0x90] = 0x0821,
                        [0x40] = 3
                    }
                }
            );
        }

        [TestMethod]
        public void SHR_sizeSEGuIMM16_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 3
                shr word[ds:0x80], word[ds:0x40]
                shr byte[ds:0x90], byte[ds:0x41]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {

                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x0864,
                        [0x90] = 0x0821,
                        [0x40] = 3
                    }
                }
            );
        }

        [TestMethod]
        public void AND_REG_REG()
        {
            AssertState(
                @"
                mov r0, 0x4321
                mov r1, 0x1234
                and r0, r1
                ",
                new CpuState
                {
                    r0 = 0x0220,
                    r1 = 0x1234
                }
            );
        }

        [TestMethod]
        public void AND_REG_IMM()
        {
            AssertState(
                @"
                mov r0, 0x4321
                and r0, 0x1234
                ",
                new CpuState
                {
                    r0 = 0x0220
                }
            );
        }

        [TestMethod]
        public void AND_REG_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r0, 0x4321
                mov r1, 0x4321
                mov r2, 0x4321
                mov r3, 0x70
                and r0, word[ds:r3+0x10]
                and r1, byte[ds:r3+0x10]
                mov r3, 0x90
                and r2, word[ds:r3-0x10]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0220,
                        r1 = 0x0000,
                        r2 = 0x0220,
                        r3 = 0x0090
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void AND_REG_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x8765
                mov r0, 0x4321
                mov r1, 0x4321
                mov r3, 0x80
                and r0, word[ds:r3]
                and r1, byte[ds:r3]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0321,
                        r1 = 0x0001,
                        r3 = 0x0080
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x8765
                    }
                }
            );
        }

        [TestMethod]
        public void AND_REG_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r0, 0x4321
                mov r1, 0x4321
                mov r2, 0x4321
                and r0, word[ds:0x80]
                and r1, byte[ds:0x80]
                and r2l, byte[ds:0x80]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0220,
                        r1 = 0x0000,
                        r2 = 0x4300
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void AND_sizeSEGREGplusIMM_REG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r0, 0x1234
                mov r2, 0xA0
                mov r3, 0x70
                and word[ds:r3+0x10], r0
                and byte[ds:r2-0x0F], r0l
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
                        [0x80] = 0x0220,
                        [0x90] = 0x4320
                    }
                }
            );
        }

        [TestMethod]
        public void AND_sizeSEGREGplusIMM_IMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r2, 0xA0
                mov r3, 0x70
                and word[ds:r3+0x10], 0x1234
                and byte[ds:r2-0x0F], 0x1234
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
                        [0x80] = 0x0220,
                        [0x90] = 0x4320
                    }
                }
            );
        }

        [TestMethod]
        public void AND_sizeSEGREGplusIMM_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r2, 0xA0
                mov r3, 0x70
                and word[ds:r3+0x10], word[ds:r3-0x30]
                and byte[ds:r2-0x0F], byte[ds:r3-0x2F]
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
                        [0x80] = 0x0220,
                        [0x90] = 0x4320,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void AND_sizeSEGREGplusIMM_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r1, 0x40
                mov r2, 0xA0
                mov r3, 0x70
                and word[ds:r3+0x10], word[ds:r1]
                mov r1, 0x41
                and byte[ds:r2-0x0F], byte[ds:r1]
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
                        [0x80] = 0x0220,
                        [0x90] = 0x4320,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void AND_sizeSEGREGplusIMM_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r2, 0xA0
                mov r3, 0x70
                and word[ds:r3+0x10], word[ds:0x40]
                and byte[ds:r2-0x0F], byte[ds:0x41]
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
                        [0x80] = 0x0220,
                        [0x90] = 0x4320,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void AND_sizeSEGREG_REG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r0, 0x1234
                mov r2, 0x80
                mov r3, 0x90
                and word[ds:r2], r0
                and byte[ds:r3], r0l
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234,
                        r2 = 0x0080,
                        r3 = 0x0090
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x0220,
                        [0x90] = 0x0021
                    }
                }
            );
        }

        [TestMethod]
        public void AND_sizeSEGREG_IMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r2, 0x80
                mov r3, 0x90
                and word[ds:r2], 0x1234
                and byte[ds:r3], 0x1234
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r2 = 0x0080,
                        r3 = 0x0090
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x0220,
                        [0x90] = 0x0021
                    }
                }
            );
        }

        [TestMethod]
        public void AND_sizeSEGREG_sizeSEGREGplusIMM()
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
                and word[ds:r2], word[ds:r0+0x10]
                and byte[ds:r3], byte[ds:r1-0x0F]
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
                        [0x80] = 0x0220,
                        [0x90] = 0x4320
                    }
                }
            );
        }

        [TestMethod]
        public void AND_sizeSEGREG_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r0, 0x40
                mov r1, 0x41
                mov r2, 0x80
                mov r3, 0x90
                and word[ds:r2], word[ds:r0]
                and byte[ds:r3], byte[ds:r1]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0040,
                        r1 = 0x0041,
                        r2 = 0x0080,
                        r3 = 0x0090
                    },
                    RAMChecks = new()
                    {
                        [0x40] = 0x1234,
                        [0x80] = 0x0220,
                        [0x90] = 0x0021
                    }
                }
            );
        }

        [TestMethod]
        public void AND_sizeSEGREG_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r2, 0x80
                mov r3, 0x90
                and word[ds:r2], word[ds:0x40]
                and byte[ds:r3], byte[ds:0x41]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r2 = 0x0080,
                        r3 = 0x0090
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x0220,
                        [0x90] = 0x0021
                    }
                }
            );
        }

        [TestMethod]
        public void AND_sizeSEGuIMM16_REG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r0, 0x1234
                and word[ds:0x80], r0
                and byte[ds:0x90], r0l
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x0220,
                        [0x90] = 0x0021
                    }
                }
            );
        }

        [TestMethod]
        public void AND_sizeSEGuIMM16_IMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                and word[ds:0x80], 0x1234
                and byte[ds:0x90], 0x1234
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {

                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x0220,
                        [0x90] = 0x0021
                    }
                }
            );
        }

        [TestMethod]
        public void AND_sizeSEGuIMM16_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r1, 0x30
                mov r2, 0x50
                and word[ds:0x80], word[ds:r1+0x10]
                and byte[ds:0x90], byte[ds:r2-0x0F]
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
                        [0x80] = 0x0220,
                        [0x90] = 0x0021,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void AND_sizeSEGuIMM16_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r1, 0x40
                mov r2, 0x41
                and word[ds:0x80], word[ds:r1]
                and byte[ds:0x90], byte[ds:r2]
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
                        [0x80] = 0x0220,
                        [0x90] = 0x0021,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void AND_sizeSEGuIMM16_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                and word[ds:0x80], word[ds:0x40]
                and byte[ds:0x90], byte[ds:0x41]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {

                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x0220,
                        [0x90] = 0x0021,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void OR_REG_REG()
        {
            AssertState(
                @"
                mov r0, 0x4321
                mov r1, 0x1234
                or  r0, r1
                ",
                new CpuState
                {
                    r0 = 0x5335,
                    r1 = 0x1234
                }
            );
        }

        [TestMethod]
        public void OR_REG_IMM()
        {
            AssertState(
                @"
                mov r0, 0x4321
                or  r0, 0x1234
                ",
                new CpuState
                {
                    r0 = 0x5335
                }
            );
        }

        [TestMethod]
        public void OR_REG_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r0, 0x4321
                mov r1, 0x4321
                mov r2, 0x4321
                mov r3, 0x70
                or  r0, word[ds:r3+0x10]
                or  r1, byte[ds:r3+0x10]
                mov r3, 0x90
                or  r2, word[ds:r3-0x10]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x5335,
                        r1 = 0x4333,
                        r2 = 0x5335,
                        r3 = 0x0090
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void OR_REG_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x8765
                mov r0, 0x4321
                mov r1, 0x4321
                mov r3, 0x80
                or  r0, word[ds:r3]
                or  r1, byte[ds:r3]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0xC765,
                        r1 = 0x43A7,
                        r3 = 0x0080
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x8765
                    }
                }
            );
        }

        [TestMethod]
        public void OR_REG_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r0, 0x4321
                mov r1, 0x4321
                mov r2, 0x4321
                or  r0, word[ds:0x80]
                or  r1, byte[ds:0x80]
                or  r2l, byte[ds:0x80]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x5335,
                        r1 = 0x4333,
                        r2 = 0x4333
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void OR_sizeSEGREGplusIMM_REG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r0, 0x1234
                mov r2, 0xA0
                mov r3, 0x70
                or  word[ds:r3+0x10], r0
                or  byte[ds:r2-0x0F], r0l
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
                        [0x80] = 0x5335,
                        [0x90] = 0x4335
                    }
                }
            );
        }

        [TestMethod]
        public void OR_sizeSEGREGplusIMM_IMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r2, 0xA0
                mov r3, 0x70
                or  word[ds:r3+0x10], 0x1234
                or  byte[ds:r2-0x0F], 0x12
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
                        [0x80] = 0x5335,
                        [0x90] = 0x4333
                    }
                }
            );
        }

        [TestMethod]
        public void OR_sizeSEGREGplusIMM_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r2, 0xA0
                mov r3, 0x70
                or  word[ds:r3+0x10], word[ds:r3-0x30]
                or  byte[ds:r2-0x0F], byte[ds:r3-0x2F]
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
                        [0x80] = 0x5335,
                        [0x90] = 0x4335,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void OR_sizeSEGREGplusIMM_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r1, 0x40
                mov r2, 0xA0
                mov r3, 0x70
                or  word[ds:r3+0x10], word[ds:r1]
                mov r1, 0x41
                or  byte[ds:r2-0x0F], byte[ds:r1]
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
                        [0x80] = 0x5335,
                        [0x90] = 0x4335,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void OR_sizeSEGREGplusIMM_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r2, 0xA0
                mov r3, 0x70
                or  word[ds:r3+0x10], word[ds:0x40]
                or  byte[ds:r2-0x0F], byte[ds:0x41]
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
                        [0x80] = 0x5335,
                        [0x90] = 0x4335,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void OR_sizeSEGREG_REG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r0, 0x1234
                mov r2, 0x80
                mov r3, 0x91
                or  word[ds:r2], r0
                or  byte[ds:r3], r0l
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
                        [0x80] = 0x5335,
                        [0x90] = 0x4335
                    }
                }
            );
        }

        [TestMethod]
        public void OR_sizeSEGREG_IMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r2, 0x80
                mov r3, 0x91
                or  word[ds:r2], 0x1234
                or  byte[ds:r3], 0x12
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
                        [0x80] = 0x5335,
                        [0x90] = 0x4333
                    }
                }
            );
        }

        [TestMethod]
        public void OR_sizeSEGREG_sizeSEGREGplusIMM()
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
                or  word[ds:r2], word[ds:r0+0x10]
                or  byte[ds:r3], byte[ds:r1-0x0F]
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
                        [0x80] = 0x5335,
                        [0x90] = 0x4335
                    }
                }
            );
        }

        [TestMethod]
        public void OR_sizeSEGREG_sizeSEGREG()
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
                or  word[ds:r2], word[ds:r0]
                or  byte[ds:r3], byte[ds:r1]
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
                        [0x40] = 0x1234,
                        [0x80] = 0x5335,
                        [0x90] = 0x4335
                    }
                }
            );
        }

        [TestMethod]
        public void OR_sizeSEGREG_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r2, 0x80
                mov r3, 0x91
                or  word[ds:r2], word[ds:0x40]
                or  byte[ds:r3], byte[ds:0x41]
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
                        [0x80] = 0x5335,
                        [0x90] = 0x4335
                    }
                }
            );
        }

        [TestMethod]
        public void OR_sizeSEGuIMM16_REG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r0, 0x1234
                or  word[ds:0x80], r0
                or  byte[ds:0x91], r0l
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x5335,
                        [0x90] = 0x4335
                    }
                }
            );
        }

        [TestMethod]
        public void OR_sizeSEGuIMM16_IMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                or  word[ds:0x80], 0x1234
                or  byte[ds:0x91], 0x12
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {

                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x5335,
                        [0x90] = 0x4333
                    }
                }
            );
        }

        [TestMethod]
        public void OR_sizeSEGuIMM16_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r1, 0x30
                mov r2, 0x50
                or  word[ds:0x80], word[ds:r1+0x10]
                or  byte[ds:0x91], byte[ds:r2-0x0F]
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
                        [0x80] = 0x5335,
                        [0x90] = 0x4335,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void OR_sizeSEGuIMM16_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r1, 0x40
                mov r2, 0x41
                or  word[ds:0x80], word[ds:r1]
                or  byte[ds:0x91], byte[ds:r2]
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
                        [0x80] = 0x5335,
                        [0x90] = 0x4335,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void OR_sizeSEGuIMM16_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                or  word[ds:0x80], word[ds:0x40]
                or  byte[ds:0x91], byte[ds:0x41]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {

                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x5335,
                        [0x90] = 0x4335,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void XOR_REG_REG()
        {
            AssertState(
                @"
                mov r0, 0x4321
                mov r1, 0x1234
                xor r0, r1
                ",
                new CpuState
                {
                    r0 = 0x5115,
                    r1 = 0x1234
                }
            );
        }

        [TestMethod]
        public void XOR_REG_IMM()
        {
            AssertState(
                @"
                mov r0, 0x4321
                xor r0, 0x1234
                ",
                new CpuState
                {
                    r0 = 0x5115
                }
            );
        }

        [TestMethod]
        public void XOR_REG_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r0, 0x4321
                mov r1, 0x4321
                mov r2, 0x4321
                mov r3, 0x70
                xor r0, word[ds:r3+0x10]
                xor r1, byte[ds:r3+0x10]
                mov r3, 0x90
                xor r2, word[ds:r3-0x10]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x5115,
                        r1 = 0x4333,
                        r2 = 0x5115,
                        r3 = 0x0090
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void XOR_REG_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x8765
                mov r0, 0x4321
                mov r1, 0x4321
                mov r3, 0x80
                xor r0, word[ds:r3]
                xor r1, byte[ds:r3]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0xC444,
                        r1 = 0x43A6,
                        r3 = 0x0080
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x8765
                    }
                }
            );
        }

        [TestMethod]
        public void XOR_REG_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r0, 0x4321
                mov r1, 0x4321
                mov r2, 0x4321
                xor r0, word[ds:0x80]
                xor r1, byte[ds:0x80]
                xor r2l, byte[ds:0x80]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x5115,
                        r1 = 0x4333,
                        r2 = 0x4333
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void XOR_sizeSEGREGplusIMM_REG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r0, 0x1234
                mov r2, 0xA0
                mov r3, 0x70
                xor word[ds:r3+0x10], r0
                xor byte[ds:r2-0x0F], r0l
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
                        [0x80] = 0x5115,
                        [0x90] = 0x4315
                    }
                }
            );
        }

        [TestMethod]
        public void XOR_sizeSEGREGplusIMM_IMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r2, 0xA0
                mov r3, 0x70
                xor word[ds:r3+0x10], 0x1234
                xor byte[ds:r2-0x0F], 0x1234
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
                        [0x80] = 0x5115,
                        [0x90] = 0x4315
                    }
                }
            );
        }

        [TestMethod]
        public void XOR_sizeSEGREGplusIMM_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r2, 0xA0
                mov r3, 0x70
                xor word[ds:r3+0x10], word[ds:r3-0x30]
                xor byte[ds:r2-0x0F], byte[ds:r3-0x2F]
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
                        [0x80] = 0x5115,
                        [0x90] = 0x4315,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void XOR_sizeSEGREGplusIMM_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r1, 0x40
                mov r2, 0xA0
                mov r3, 0x70
                xor word[ds:r3+0x10], word[ds:r1]
                mov r1, 0x41
                xor byte[ds:r2-0x0F], byte[ds:r1]
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
                        [0x80] = 0x5115,
                        [0x90] = 0x4315,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void XOR_sizeSEGREGplusIMM_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r2, 0xA0
                mov r3, 0x70
                xor word[ds:r3+0x10], word[ds:0x40]
                xor byte[ds:r2-0x0F], byte[ds:0x41]
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
                        [0x80] = 0x5115,
                        [0x90] = 0x4315,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void XOR_sizeSEGREG_REG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r0, 0x1234
                mov r2, 0x80
                mov r3, 0x91
                xor word[ds:r2], r0
                xor byte[ds:r3], r0l
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
                        [0x80] = 0x5115,
                        [0x90] = 0x4315
                    }
                }
            );
        }

        [TestMethod]
        public void XOR_sizeSEGREG_IMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r2, 0x80
                mov r3, 0x91
                xor word[ds:r2], 0x1234
                xor byte[ds:r3], 0x12
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
                        [0x80] = 0x5115,
                        [0x90] = 0x4333
                    }
                }
            );
        }

        [TestMethod]
        public void XOR_sizeSEGREG_sizeSEGREGplusIMM()
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
                xor word[ds:r2], word[ds:r0+0x10]
                xor byte[ds:r3], byte[ds:r1-0x0F]
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
                        [0x80] = 0x5115,
                        [0x90] = 0x4315
                    }
                }
            );
        }

        [TestMethod]
        public void XOR_sizeSEGREG_sizeSEGREG()
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
                xor word[ds:r2], word[ds:r0]
                xor byte[ds:r3], byte[ds:r1]
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
                        [0x40] = 0x1234,
                        [0x80] = 0x5115,
                        [0x90] = 0x4315
                    }
                }
            );
        }

        [TestMethod]
        public void XOR_sizeSEGREG_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r2, 0x80
                mov r3, 0x91
                xor word[ds:r2], word[ds:0x40]
                xor byte[ds:r3], byte[ds:0x41]
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
                        [0x80] = 0x5115,
                        [0x90] = 0x4315
                    }
                }
            );
        }

        [TestMethod]
        public void XOR_sizeSEGuIMM16_REG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov r0, 0x1234
                xor word[ds:0x80], r0
                xor byte[ds:0x91], r0l
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x5115,
                        [0x90] = 0x4315
                    }
                }
            );
        }

        [TestMethod]
        public void XOR_sizeSEGuIMM16_IMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                xor word[ds:0x80], 0x1234
                xor byte[ds:0x90], 0x12
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {

                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x5115,
                        [0x90] = 0x5121
                    }
                }
            );
        }

        [TestMethod]
        public void XOR_sizeSEGuIMM16_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r1, 0x30
                mov r2, 0x50
                xor word[ds:0x80], word[ds:r1+0x10]
                xor byte[ds:0x91], byte[ds:r2-0x0F]
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
                        [0x80] = 0x5115,
                        [0x90] = 0x4315,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void XOR_sizeSEGuIMM16_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                mov r1, 0x40
                mov r2, 0x41
                xor word[ds:0x80], word[ds:r1]
                xor byte[ds:0x91], byte[ds:r2]
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
                        [0x80] = 0x5115,
                        [0x90] = 0x4315,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void XOR_sizeSEGuIMM16_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x90], 0x4321
                mov word[ds:0x40], 0x1234
                xor word[ds:0x80], word[ds:0x40]
                xor byte[ds:0x90], byte[ds:0x40]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {

                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x5115,
                        [0x90] = 0x5121,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void CMP_REG_REG()
        {
            AssertState(
                @"
                mov r0, 0x1234
                mov r1, 0x4321
                cmp r0, r1
                ",
                new CpuState
                {
                    r0 = 0x1234,
                    r1 = 0x4321,
                    flags = 0x03        // zf CF SF
                }
            );

            AssertState(
                @"
                mov r0, 0x1234
                mov r1, 0x1234
                cmp r0, r1
                ",
                new CpuState
                {
                    r0 = 0x1234,
                    r1 = 0x1234,
                    flags = 0x04        // ZF cf sf
                }
            );
        }

        [TestMethod]
        public void CMP_REG_IMM()
        {
            AssertState(
                @"
                mov r0, 0x1234
                cmp r0, 0x4321
                ",
                new CpuState
                {
                    r0 = 0x1234,
                    flags = 0x03        // zf CF SF
                }
            );

            AssertState(
                @"
                mov r0, 0x1234
                cmp r0, 0x1234
                ",
                new CpuState
                {
                    r0 = 0x1234,
                    flags = 0x04        // ZF cf sf
                }
            );

            AssertState(
                @"
                mov r0, 0x12
                cmp r0, 0x43
                ",
                new CpuState
                {
                    r0 = 0x0012,
                    flags = 0x03        // zf CF SF
                }
            );

            AssertState(
                @"
                mov r0, 0x12
                cmp r0, 0x12
                ",
                new CpuState
                {
                    r0 = 0x0012,
                    flags = 0x04        // ZF cf sf
                }
            );
        }

        [TestMethod]
        public void CMP_REG_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r0, 0x1234
                mov r2, 0x70
                cmp r0, word[ds:r2+0x10]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234,
                        r2 = 0x0070,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r1, 0x1234
                mov r3, 0x70
                cmp r1l, byte[ds:r3+0x11]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r1 = 0x1234,
                        r3 = 0x0070,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov r0, 0x1234
                mov r3, 0x70
                cmp r0, word[ds:r3+0x10]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234,
                        r3 = 0x0070,
                        flags = 0x0003      // zf CF SF
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov r1, 0x1234
                mov r3, 0x70
                cmp r1, byte[ds:r3+0x11]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r1 = 0x1234,
                        r3 = 0x0070,
                        flags = 0x0000      // zf cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321
                    }
                }
            );
        }

        [TestMethod]
        public void CMP_REG_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r0, 0x1234
                mov r3, 0x80
                cmp r0, word[ds:r3]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234,
                        r3 = 0x0080,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r1, 0x1234
                mov r3, 0x81
                cmp r1l, byte[ds:r3]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r1 = 0x1234,
                        r3 = 0x0081,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov r0, 0x1234
                mov r3, 0x80
                cmp r0, word[ds:r3]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234,
                        r3 = 0x0080,
                        flags = 0x0003      // zf CF SF
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov r1, 0x1234
                mov r3, 0x81
                cmp r1, byte[ds:r3]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r1 = 0x1234,
                        r3 = 0x0081,
                        flags = 0x0000      // zf cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321
                    }
                }
            );
        }

        [TestMethod]
        public void CMP_REG_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r0, 0x1234
                cmp r0, word[ds:0x80]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r1, 0x1234
                cmp r1l, byte[ds:0x81]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r1 = 0x1234,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov r0, 0x1234
                cmp r0, word[ds:0x80]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234,
                        flags = 0x0003      // zf CF SF
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov r1, 0x1234
                cmp r1l, byte[ds:0x80]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r1 = 0x1234,
                        flags = 0x0003      // zf CF SF
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321
                    }
                }
            );
        }

        [TestMethod]
        public void CMP_sizeSEGREGplusIMM_REG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r0, 0x1234
                mov r3, 0x70
                cmp word[ds:r3+0x10], r0
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234,
                        r3 = 0x0070,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r0, 0x1234
                mov r2, 0xA0
                cmp byte[ds:r2-0x0F], r0l
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234,
                        r2 = 0x00A0,
                        flags = 0x0003      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov r0, 0x1234
                mov r2, 0x70
                cmp word[ds:r2+0x10], r0
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234,
                        r2 = 0x0070,
                        flags = 0x0000      // zf cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov r0, 0x1234
                mov r2, 0xA0
                cmp byte[ds:r2-0x0F], r0l
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234,
                        r2 = 0x00A0,
                        flags = 0x0003      // zf CF SF
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321
                    }
                }
            );
        }

        [TestMethod]
        public void CMP_sizeSEGREGplusIMM_IMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r3, 0x70
                cmp word[ds:r3+0x10], 0x1234
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r3 = 0x0070,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r2, 0x90
                cmp byte[ds:r2-0x0F], 0x34
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r2 = 0x0090,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov r3, 0x70
                cmp word[ds:r3+0x10], 0x1234
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r3 = 0x0070,
                        flags = 0x0000      // zf cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov r2, 0x90
                cmp byte[ds:r2-0x0F], 0x34
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r2 = 0x0090,
                        flags = 0x0003      // zf CF SF
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321
                    }
                }
            );
        }

        [TestMethod]
        public void CMP_sizeSEGREGplusIMM_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov word[ds:0x40], 0x1234
                mov r3, 0x70
                cmp word[ds:r3+0x10], word[ds:r3-0x30]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r3 = 0x0070,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov word[ds:0x40], 0x1234
                mov r2, 0x90
                mov r3, 0x70
                cmp byte[ds:r2-0x0F], byte[ds:r3-0x2F]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r2 = 0x0090,
                        r3 = 0x0070,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x40], 0x1234
                mov r3, 0x70
                cmp word[ds:r3+0x10], word[ds:r3-0x30]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r3 = 0x0070,
                        flags = 0x0000      // zf cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x40], 0x1234
                mov r2, 0x90
                mov r3, 0x70
                cmp byte[ds:r2-0x0F], byte[ds:r3-0x2F]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r2 = 0x0090,
                        r3 = 0x0070,
                        flags = 0x0003      // zf CF SF
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void CMP_sizeSEGREGplusIMM_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov word[ds:0x40], 0x1234
                mov r1, 0x40
                mov r3, 0x70
                cmp word[ds:r3+0x10], word[ds:r1]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r1 = 0x0040,
                        r3 = 0x0070,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov word[ds:0x40], 0x1234
                mov r1, 0x41
                mov r2, 0x90
                cmp byte[ds:r2-0x0F], byte[ds:r1]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r1 = 0x0041,
                        r2 = 0x0090,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x40], 0x1234
                mov r1, 0x40
                mov r3, 0x70
                cmp word[ds:r3+0x10], word[ds:r1]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r1 = 0x0040,
                        r3 = 0x0070,
                        flags = 0x0000      // zf cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x40], 0x1234
                mov r1, 0x41
                mov r2, 0x90
                cmp byte[ds:r2-0x0F], byte[ds:r1]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r1 = 0x0041,
                        r2 = 0x0090,
                        flags = 0x0003      // zf CF SF
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void CMP_sizeSEGREGplusIMM_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov word[ds:0x40], 0x1234
                mov r3, 0x70
                cmp word[ds:r3+0x10], word[ds:0x40]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r3 = 0x0070,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov word[ds:0x40], 0x1234
                mov r2, 0x90
                cmp byte[ds:r2-0x0F], byte[ds:0x41]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r2 = 0x0090,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x40], 0x1234
                mov r3, 0x70
                cmp word[ds:r3+0x10], word[ds:0x40]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r3 = 0x0070,
                        flags = 0x0000      // zf cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x40], 0x1234
                mov r2, 0xA0
                cmp byte[ds:r2-0x0F], byte[ds:0x41]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r2 = 0x00A0,
                        flags = 0x0003      // zf CF SF
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void CMP_sizeSEGREG_REG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r0, 0x1234
                mov r2, 0x80
                cmp word[ds:r2], r0
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234,
                        r2 = 0x0080,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r0, 0x1234
                mov r3, 0x81
                cmp byte[ds:r3], r0l
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234,
                        r3 = 0x0081,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov r0, 0x1234
                mov r2, 0x80
                cmp word[ds:r2], r0
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234,
                        r2 = 0x0080,
                        flags = 0x0000      // zf cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov r0, 0x1234
                mov r3, 0x81
                cmp byte[ds:r3], r0l
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234,
                        r3 = 0x0081,
                        flags = 0x0003      // zf CF SF
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321
                    }
                }
            );
        }

        [TestMethod]
        public void CMP_sizeSEGREG_IMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r2, 0x80
                cmp word[ds:r2], 0x1234
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r2 = 0x0080,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r3, 0x81
                cmp byte[ds:r3], 0x34
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r3 = 0x0081,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov r2, 0x80
                cmp word[ds:r2], 0x1234
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r2 = 0x0080,
                        flags = 0x0000      // zf cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov r3, 0x81
                cmp byte[ds:r3], 0x34
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r3 = 0x0081,
                        flags = 0x0003      // zf CF SF
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321
                    }
                }
            );
        }

        [TestMethod]
        public void CMP_sizeSEGREG_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov word[ds:0x40], 0x1234
                mov r0, 0x30
                mov r2, 0x80
                cmp word[ds:r2], word[ds:r0+0x10]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0030,
                        r2 = 0x0080,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov word[ds:0x40], 0x1234
                mov r1, 0x50
                mov r3, 0x81
                cmp byte[ds:r3], byte[ds:r1-0x0F]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r1 = 0x0050,
                        r3 = 0x0081,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x40], 0x1234
                mov r0, 0x30
                mov r2, 0x80
                cmp word[ds:r2], word[ds:r0+0x10]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0030,
                        r2 = 0x0080,
                        flags = 0x0000      // zf cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x40], 0x1234
                mov r1, 0x50
                mov r3, 0x81
                cmp byte[ds:r3], byte[ds:r1-0x0F]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r1 = 0x0050,
                        r3 = 0x0081,
                        flags = 0x0003      // zf CF SF
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void CMP_sizeSEGREG_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov word[ds:0x40], 0x1234
                mov r0, 0x40
                mov r2, 0x80
                cmp word[ds:r2], word[ds:r0]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0040,
                        r2 = 0x0080,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov word[ds:0x40], 0x1234
                mov r1, 0x41
                mov r3, 0x81
                cmp byte[ds:r3], byte[ds:r1]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r1 = 0x0041,
                        r3 = 0x0081,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x40], 0x1234
                mov r0, 0x40
                mov r2, 0x80
                cmp word[ds:r2], word[ds:r0]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0040,
                        r2 = 0x0080,
                        flags = 0x0000      // zf cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x40], 0x1234
                mov r1, 0x41
                mov r3, 0x81
                cmp byte[ds:r3], byte[ds:r1]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r1 = 0x0041,
                        r3 = 0x0081,
                        flags = 0x0003      // zf CF SF
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void CMP_sizeSEGREG_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov word[ds:0x40], 0x1234
                mov r2, 0x80
                cmp word[ds:r2], word[ds:0x40]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r2 = 0x0080,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov word[ds:0x40], 0x1234
                mov r3, 0x81
                cmp byte[ds:r3], byte[ds:0x41]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r3 = 0x0081,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x40], 0x1234
                mov r2, 0x80
                cmp word[ds:r2], word[ds:0x40]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r2 = 0x0080,
                        flags = 0x0000      // zf cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x40], 0x1234
                mov r3, 0x91
                cmp byte[ds:r3], byte[ds:0x41]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r3 = 0x0091,
                        flags = 0x0003      // zf CF SF
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void CMP_sizeSEGuIMM16_REG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r0, 0x1234
                cmp word[ds:0x80], r0
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov r0, 0x1234
                cmp byte[ds:0x81], r0l
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234,
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov r0, 0x1234
                cmp word[ds:0x80], r0
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234,
                        flags = 0x0000      // zf cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov r0, 0x1234
                cmp byte[ds:0x81], r0l
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234,
                        flags = 0x0003      // zf CF SF
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321
                    }
                }
            );
        }

        [TestMethod]
        public void CMP_sizeSEGuIMM16_IMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                cmp word[ds:0x80], 0x1234
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                cmp byte[ds:0x81], 0x34
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        flags = 0x0004      // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                cmp word[ds:0x80], 0x1234
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        flags = 0x0000      // zf cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                cmp byte[ds:0x81], 0x34
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        flags = 0x0003      // zf CF SF
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321
                    }
                }
            );
        }

        [TestMethod]
        public void CMP_sizeSEGuIMM16_sizeSEGREGplusIMM()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov word[ds:0x40], 0x1234
                mov r1, 0x30
                cmp word[ds:0x80], word[ds:r1+0x10]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r1 = 0x0030,
                        flags = 0x0004        // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov word[ds:0x40], 0x1234
                mov r3, 0x50
                cmp byte[ds:0x81], byte[ds:r3-0x0F]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r3 = 0x0050,
                        flags = 0x0004        // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x40], 0x1234
                mov r1, 0x30
                cmp word[ds:0x80], word[ds:r1+0x10]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r1 = 0x0030,
                        flags = 0x0000        // zf cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x40], 0x1234
                mov r2, 0x50
                cmp byte[ds:0x81], byte[ds:r2-0x0F]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r2 = 0x0050,
                        flags = 0x0003        // zf CF SF
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void CMP_sizeSEGuIMM16_sizeSEGREG()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov word[ds:0x40], 0x1234
                mov r1, 0x40
                cmp word[ds:0x80], word[ds:r1]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r1 = 0x40,
                        flags = 0x0004        // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov word[ds:0x40], 0x1234
                mov r3, 0x41
                cmp byte[ds:0x81], byte[ds:r3]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r3 = 0x41,
                        flags = 0x0004        // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x40], 0x1234
                mov r1, 0x40
                cmp word[ds:0x80], word[ds:r1]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r1 = 0x40,
                        flags = 0x0000        // zf cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x40], 0x1234
                mov r3, 0x41
                cmp byte[ds:0x81], byte[ds:r3]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r3 = 0x41,
                        flags = 0x0003        // zf CF SF
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void CMP_sizeSEGuIMM16_sizeSEGuIMM16()
        {
            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov word[ds:0x40], 0x1234
                cmp word[ds:0x80], word[ds:0x40]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        flags = 0x0004        // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x1234
                mov word[ds:0x40], 0x1234
                cmp byte[ds:0x81], byte[ds:0x41]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        flags = 0x0004        // ZF cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x40], 0x1234
                cmp word[ds:0x80], word[ds:0x40]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        flags = 0x0000        // zf cf sf
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321,
                        [0x40] = 0x1234
                    }
                }
            );

            AssertState(
                @"
                mov word[ds:0x80], 0x4321
                mov word[ds:0x40], 0x1234
                cmp byte[ds:0x81], byte[ds:0x41]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        flags = 0x0003        // zf CF SF
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x4321,
                        [0x40] = 0x1234
                    }
                }
            );
        }

        [TestMethod]
        public void BIT_REG_REG()
        {
            AssertState(
                @"
                mov r0, 0x1234
                mov r1, 0x02
                bit r0, r1
                ",
                new CpuState
                {
                    r0 = 0x1234,
                    r1 = 0x0002,
                    flags = 0x04        // ZF cf sf
                }
            );

            AssertState(
                @"
                mov r0, 0x1234
                mov r1, 0x04
                bit r0, r1
                ",
                new CpuState
                {
                    r0 = 0x1234,
                    r1 = 0x0004,
                    flags = 0x00        // zf cf sf
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

        [TestMethod]
        public void CALL_IMM()
        {
            AssertState(
                @"
                mov r0, 0x20
                mov r1, 0x3A
                call subroutine
                hlt

                subroutine:
                    add r0, r1
                    ret
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x5A,
                        r1 = 0x3A
                    },
                    RAMChecks = new()
                    {
                        
                    }
                }
            );
        }

        [TestMethod]
        public void PUSH_REG()
        {
            AssertState(
                @"
                mov r0, 0x1234
                push r0
                pop r1
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234,
                        r1 = 0x1234
                    },
                    RAMChecks = new()
                    {
                        
                    }
                }
            );
        }

        [TestMethod]
        public void POP_REG()
        {
            AssertState(
                @"
                mov r0, 0x1234
                push r0
                pop r1
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x1234,
                        r1 = 0x1234
                    },
                    RAMChecks = new()
                    {
                        
                    }
                }
            );
        }

        [TestMethod]
        public void DEC_REG()
        {
            AssertState(
                @"
                mov r0, 0x10
                dec r0
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0F
                    },
                    RAMChecks = new()
                    {
                        
                    }
                }
            );
        }

        [TestMethod]
        public void INC_REG()
        {
            AssertState(
                @"
                mov r0, 0x10
                inc r0
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x11
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