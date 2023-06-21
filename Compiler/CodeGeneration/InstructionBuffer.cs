using System;

namespace Compiler.CodeGeneration
{
    internal sealed class InstructionBuffer
    {
        public List<Instruction> Instructions => instructions; // TODO Implement interface on base

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