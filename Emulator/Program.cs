﻿namespace Emulator
{
    internal static class Program
    {
        public static void Main(string[] args)
        {
            MicrocodeRom microcodeRom;
            try
            {
                microcodeRom = Microcode.AssembleRom(@"X:\\Cobalt\\Emulator\\Microcode.cmc");
                var data = MicrocodeRom.ToRomBinary(microcodeRom);
                File.WriteAllBytes(@"C:\\Temp\\Cobalt.bin", data);
            }
            catch (AssemblyException ex)
            {
                Console.WriteLine($"Line {ex.Line}: {ex.Message}");
                return;
            }

            var machine = new Machine(microcodeRom) { DebugOutput = true };

            var assembler = new Assembler(microcodeRom);
            var program = assembler.AssembleSource(
                "nop\n" +
                @"
                mov [0x80], 0x1234
                mov [0x90], 0x1234
                mov r0, 0x80
                mov r1, [r0]
                mov r2, [r0+0x10]
                " +
                "hlt"
            );
            for (ushort i = 0; i < program.Length; ++i)
                machine.RAM.WriteByte(0, i, program[i]);

            File.WriteAllBytes(@"C:\\Temp\Program.bin", program);

            machine.Run();
        }
    }
}