using System;

namespace Compiler.CodeGeneration
{
    internal sealed class InstructionBuffer
    {
        public List<Instruction> Instructions => instructions; // TODO Implement interface on base

        public IReadOnlyList<Label> Labels => labels;

        private readonly List<Label> labels;

        private readonly List<Instruction> instructions;

        public InstructionBuffer()
        {
            instructions = new List<Instruction>(4);
            labels = new List<Label>(4);
        }

        public void HACK_Optmize()
        {
            instructions.RemoveAll(x =>
            {
                return x.Opcode == Opcode.Move
                       && x.A.Type == x.B.Type
                       && x.A.Value == x.B.Value;
            });
        }

        public Label AllocateLabel() // TODO Should be moved to CurrentFunction, perhaps
        {
            var label = new Label(this, labels.Count);
            labels.Add(label);

            return label;
        }

        public void Emit(Opcode opcode)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode
            });
        }

        public void EmitO(Opcode opcode, Operand oprA)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = oprA
            });
        }

        public void EmitOO(Opcode opcode, Operand oprA, Operand oprB)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = oprA,
                B = oprB
            });
        }

        public void EmitOA(Opcode opcode, Operand oprA, IReadOnlyList<Operand>? argC)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = oprA,
                C = argC
            });
        }

        public void EmitL(Opcode opcode, Label label)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.Label, Size = - 1, Value = label.Index }
            });
        }
    }

    internal sealed class Label
    {
        public int Index { get; } // TODO Better names

        public int Location { get; /* private */ set; } // HACK TODO Need to be private

        private readonly InstructionBuffer buffer;

        public Label(InstructionBuffer buffer, int index)
        {
            Index = index;
            this.buffer = buffer;

            Location = -1;
        }

        public void Mark()
        {
            if (Location >= 0) throw new InvalidOperationException("Label is already marked");

            Location = buffer.Instructions.Count;
        }
    }
}