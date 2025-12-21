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

        // TODO Reimplement correctly later. This implementation does not update label offsets.
        //public void HACK_Optmize()
        //{
        //    instructions.RemoveAll(x =>
        //    {
        //        return x.Opcode == Opcode.Move
        //               && x.A.Type == x.B.Type
        //               && x.A.Value == x.B.Value;
        //    });
        //}

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

        public void Emit(Opcode opcode, Operand oprA)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = oprA
            });
        }

        public void Emit(Opcode opcode, Operand oprA, Operand oprB)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = oprA,
                B = oprB
            });
        }

        public void Emit(Opcode opcode, Operand oprA, Operand oprB, Operand oprC)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = oprA,
                B = oprB,
                C = oprC
            });
        }

        public void Emit(Opcode opcode, Operand oprA, IReadOnlyList<Operand>? argD)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = oprA,
                D = argD
            });
        }

        public void Emit(Opcode opcode, Operand oprA, Operand oprB, IReadOnlyList<Operand>? argD)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = oprA,
                B = oprB,
                D = argD
            });
        }

        public void Emit(Opcode opcode, Label label)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = new Operand { Type = OperandType.Label, Size = -1, Value = label.Index }
            });
        }

        public void Emit(Opcode opcode, Operand oprA, Label label)
        {
            instructions.Add(new Instruction
            {
                Opcode = opcode,
                A = oprA,
                B = new Operand { Type = OperandType.Label, Size = -1, Value = label.Index }
            });
        }
    }

    internal sealed class Label
    {
        public int Index { get; } // TODO Better names

        public int Location { get; private set; }

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

        public override string ToString()
        {
            return $".label_{Index}";
        }
    }
}