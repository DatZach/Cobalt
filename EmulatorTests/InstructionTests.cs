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