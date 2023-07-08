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
        public void MOV_REG_REGIMM16()
        {
            AssertState(
                @"
                mov [0x80], 0x1234
                mov [0x90], 0x1234
                mov r0, 0x80
                mov r1, [r0]
                mov r2, [r0+0x10]
                ",
                new MachineState
                {
                    CPU = new CpuState
                    {
                        r0 = 0x0080,
                        r1 = 0x1234,
                        r2 = 0x1234
                    },
                    RAMChecks = new()
                    {
                        [0x80] = 0x1234,
                        [0x90] = 0x1234
                    }
                }
            );
        }

        private void AssertState(string source, MachineState expectedState)
        {
            var machine = new Machine(microcodeRom) { ShutdownWhenHalted = true };

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