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
            RunMachine(
                @"
                mov r0, 4660
                mov r1, r0
                ",
                new MachineState
                {
                    r0 = new Register { Word = 0x1234 },
                    r1 = new Register { Word = 0x1234 }
                }
            );
        }

        private void RunMachine(string source, MachineState expected)
        {
            var machine = new Machine(microcodeRom) { ShutdownWhenHalted = true };

            var assembler = new Assembler(microcodeRom);
            var program = assembler.AssembleSource("nop\n" + source + "\nhlt");
            for (ushort i = 0; i < program.Length; ++i)
                machine.RAM.WriteByte(0, i, program[i]);

            machine.Run();

            var stateString = machine.CPU.ToString(); // HACK TODO No
            var expectedString = expected.ToString();

            if (!stateString.StartsWith(expectedString))
                Assert.Fail($"EXPECTED\n{expectedString}\n\nACTUAL\n{stateString}");
        }
    }

    public sealed record MachineState
    {
        public Register r0 { get; init; }

        public Register r1 { get; init; }

        public override string ToString()
        {
            return $"r0 = {r0} r1 = {r1}";
        }
    }
}