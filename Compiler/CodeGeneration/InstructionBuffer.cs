using System;

namespace Compiler.CodeGeneration
{
    internal sealed class InstructionBuffer
    {
        public byte PlatformWordWidth { get; }

        public List<Instruction> Instructions => instructions; // TODO Implement interface on base

        private readonly List<Label> labels;

        private readonly List<Instruction> instructions;

        public InstructionBuffer()
        {
            PlatformWordWidth = 32; // TODO Don't hardcode

            instructions = new List<Instruction>(4);
            labels = new List<Label>(4);
        }

        public void FixLabels()
        {
            for (var i = 0; i < labels.Count; ++i)
                labels[i].Fix();
        }

        public void Emit(Opcode opcode)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode
            });
        }

        public void EmitR(Opcode opcode, int regA)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.Register, Size = PlatformWordWidth, Value = regA }
            });
        }

        public void EmitRR(Opcode opcode, int regA, int regB)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.Register, Size = PlatformWordWidth, Value = regA },
                B = new Operand { Type = OperandType.Register, Size = PlatformWordWidth, Value = regB }
            });
        }

        public void EmitRsRs(Opcode opcode, int regA, int sizeA, int regB, int sizeB)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.Register, Size = (byte)sizeA, Value = regA },
                B = new Operand { Type = OperandType.Register, Size = (byte)sizeB, Value = regB }
            });
        }

        public void EmitRI(Opcode opcode, int regA, long immB)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.Register, Size = PlatformWordWidth, Value = regA },
                B = new Operand { Type = OperandType.ImmediateSigned, Size = PlatformWordWidth, Value = immB }
            });
        }
        public void EmitRIf(Opcode opcode, int regA, long immB, int immBWidth)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.Register, Size = (byte)immBWidth, Value = regA },
                B = new Operand { Type = OperandType.ImmediateFloat, Size = (byte)immBWidth, Value = immB }
            });
        }

        public void EmitI(Opcode opcode, long immA)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.ImmediateSigned, Size = PlatformWordWidth, Value = immA }
            });
        }

        public void EmitI(Opcode opcode, ulong immA)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.ImmediateUnsigned, Size = PlatformWordWidth, Value = (long)immA }
            });
        }

        public void EmitI(Opcode opcode, double immA)
        {
            var value = BitConverter.DoubleToInt64Bits(immA);
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.ImmediateUnsigned, Size = PlatformWordWidth, Value = value }
            });
        }

        public void EmitLR(Opcode opcode, int locA, int regB, int size)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.Local, Size = (byte)size, Value = locA },
                B = new Operand { Type = OperandType.Register, Size = (byte)size, Value = regB }
            });
        }

        public void EmitG(Opcode opcode, int globA)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.Global, Size = PlatformWordWidth, Value = globA }
            });
        }

        public void EmitRL(Opcode opcode, int regA, int locB, int size)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.Register, Size = (byte)size, Value = regA },
                B = new Operand { Type = OperandType.Local, Size = (byte)size, Value = locB }
            });
        }

        public void EmitRG(Opcode opcode, int regA, int globB, int size)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.Register, Size = (byte)size, Value = regA },
                B = new Operand { Type = OperandType.Global, Size = (byte)size, Value = globB }
            });
        }

        //public void EmitF(Opcode opcode, int funA)
        //{
        //    instructions.Add(new Instruction
        //    {
        //        Opcode = opcode,
        //        A = new Operand { Type = OperandType.Function, Size = PlatformWordWidth, Value = funA }
        //    });
        //}

        //public void EmitRF(Opcode opcode, int regA, int funB)
        //{
        //    instructions.Add(new Instruction
        //    {
        //        Opcode = opcode,
        //        A = new Operand { Type = OperandType.Register, Size = PlatformWordWidth, Value = regA },
        //        B = new Operand { Type = OperandType.Function, Size = PlatformWordWidth, Value = funB }
        //    });
        //}

        public void EmitRA(Opcode opcode, int regA, IReadOnlyList<Operand>? argC)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.Register, Size = PlatformWordWidth, Value = regA },
                C = argC
            });
        }

        public void EmitRA(Opcode opcode, int regA, int argB, int size)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.Register, Size = (byte)size, Value = regA },
                B = new Operand { Type = OperandType.Argument, Size = (byte)size, Value = argB }
            });
        }
    }

    internal sealed class Label
    {
        private int labelOffset;

        private readonly InstructionBuffer buffer;
        private readonly List<int> patches;

        public Label(InstructionBuffer buffer)
        {
            this.buffer = buffer;
            patches = new List<int>();
            labelOffset = 0;
        }

        public void Mark()
        {
            labelOffset = buffer.Instructions.Count;
        }

        public void PatchHere()
        {
            //buffer.Instructions.Last().Operand.IntValue = 0;
            patches.Add(buffer.Instructions.Count - 1);
        }

        public void Fix()
        {
            for (var i = 0; i < patches.Count; i++)
            {
                //var offset = patches[i];
                //buffer.Instructions[offset].A!.Value = labelOffset;
            }
        }

        public void ClearPatches()
        {
            patches.Clear();
        }
    }
}