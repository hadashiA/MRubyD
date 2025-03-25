using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using MRubyD.Internals;
using static Utf8StringInterpolation.Utf8String;

namespace MRubyD;

partial class MRubyState
{
    internal void CodeDump(Irep irep, IBufferWriter<byte> writer)
    {
        void WriteRegister(int n)
        {
            if (n == 0) return;
            if (n >= irep.LocalVariables.Length) return;
            var local = irep.LocalVariables[n - 1];
            if (local.Value != 0)
            {
                Format(writer, $"R{n}:{symbolTable.NameOf(local)}\n");
            }
        }

        void WriteLocalVariableA(short a)
        {
            if (a == 0 || a >= irep.LocalVariables.Length)
            {
                writer.Write("\n"u8);
                return;
            }

            writer.Write("\t;"u8);
            WriteRegister(a);
            writer.Write("\n"u8);
        }

        void WriteLocalVariableAB(short a, short b)
        {
            if (a + b == 0 || (a >= irep.LocalVariables.Length && b >= irep.LocalVariables.Length))
            {
                writer.Write("\n"u8);
                return;
            }

            writer.Write("\t;"u8);
            if (a > 0) WriteRegister(a);
            if (b > 0) WriteRegister(b);
            writer.Write("\n"u8);
        }

        Format(writer, $"irep: {Unsafe.As<Irep, nuint>(ref irep)} nregs={irep.RegisterVariableCount} nlocals={irep.LocalVariables.Length} ");
        Format(writer, $"pools={irep.PoolValues.Length} syms={irep.Symbols.Length} reps={irep.Children.Length} iren={irep.Sequence.Length}\n");


        {
            var head = false;
            for (var index = 0; index < irep.LocalVariables.Length; index++)
            {
                var localSymbol = irep.LocalVariables[index];
                var name = symbolTable.NameOf(localSymbol);
                if (name.IsEmpty) continue;
                if (!head)
                {
                    writer.Write("local variable names:\n"u8);
                    head = true;
                }

                Format(writer, $"R{index}:{name}\n");
            }
        }

        {
            int e = 0;
            for (int i = irep.CatchHandlers.Length; i > 0; i--, e++)
            {
                var catchHandler = irep.CatchHandlers[e];

                var typeName = catchHandler.HandlerType switch
                {
                    CatchHandlerType.Rescue => "rescue",
                    CatchHandlerType.Ensure => "ensure",
                    CatchHandlerType.All => "all",
                    _ => "unknown"
                };
                Format(writer, $"catch type: {typeName} begin: {catchHandler.Begin} end: {catchHandler.End} target: {catchHandler.Target}\n");
            }
        }
        {
            var pc = 0;
            var endPc = irep.Sequence.Length;
            while (pc < endPc)
            {
                //TODO: Irep debug info

                var opcode = (OpCode)irep.Sequence[pc];
                switch (opcode)
                {
                    case OpCode.Nop:
                        OperandZ.Read(irep.Sequence, ref pc);
                        writer.Write("NOP\n"u8);
                        break;
                    case OpCode.Move:
                        var bb = OperandBB.Read(irep.Sequence, ref pc);
                        Format(writer, $"MOVE\t\tR{bb.A}\tR{bb.B}\n");
                        break;
                    case OpCode.LoadL:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"LOADL\t\tR{bb.A}");
                            var value = irep.PoolValues[bb.B];
                            switch (value.VType)
                            {
                                case MRubyVType.Float:
                                    Format(writer, $"L[{value.FloatValue}]\t");
                                    break;
                                case MRubyVType.Integer:
                                    Format(writer, $"L[{value.IntegerValue}]\t");
                                    break;
                                default:
                                    Format(writer, $"L[{bb.B}]\t");
                                    break;
                            }

                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.LoadI8:
                        bb = OperandBB.Read(irep.Sequence, ref pc);
                        Format(writer, $"LOADI8\t\tR{bb.A}\t{bb.B}\t");
                        WriteLocalVariableA(bb.A);
                        break;
                    case OpCode.LoadINeg:
                        bb = OperandBB.Read(irep.Sequence, ref pc);
                        Format(writer, $"LOADINEG\t\tR{bb.A}\t-{bb.B}\t");
                        WriteLocalVariableA(bb.A);
                        break;
                    case OpCode.LoadI16:
                        var bs = OperandBS.Read(irep.Sequence, ref pc);
                        Format(writer, $"LOADI16\t\tR{bs.A}\t{bs.B}\t");
                        WriteLocalVariableA(bs.A);
                        break;
                    case OpCode.LoadI32:
                        var bss = OperandBSS.Read(irep.Sequence, ref pc);
                        Format(writer, $"LOADI32\t\tR{bss.A}\t{((ushort)bss.B << 16) | (ushort)bss.C}\t");
                        WriteLocalVariableA(bss.A);
                        break;
                    case OpCode.LoadI__1:
                        var b = OperandB.Read(irep.Sequence, ref pc);
                        Format(writer, $"LOADI__1\t\tR{b.A}\t(-1)\t");
                        WriteLocalVariableA(b.A);
                        break;
                    case OpCode.LoadI_0:
                    case OpCode.LoadI_1:
                    case OpCode.LoadI_2:
                    case OpCode.LoadI_3:
                    case OpCode.LoadI_4:
                    case OpCode.LoadI_5:
                    case OpCode.LoadI_6:
                    case OpCode.LoadI_7:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            var value = (int)opcode - (int)OpCode.LoadI_0;
                            Format(writer, $"LOADI_{value}\t\tR{b.A}\t{value}\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.LoadSym:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"LOADSYM\tR{bb.A}\t:{symbolTable.NameOf(irep.Symbols[bb.B])}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.LoadNil:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"LOADNIL\t\tR{b.A}\t(nil)\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.LoadSelf:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"LOADSELF\tR{b.A}\t(R0)\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.LoadT:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"LOADT\t\tR{b.A}\t(true)\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.LoadF:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"LOADF\t\tR{b.A}\t(false)\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.GetGV:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"GETGV\t\tR{bb.A}\t{symbolTable.NameOf(irep.Symbols[bb.B])}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.SetGV:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"SETGV\t\t{symbolTable.NameOf(irep.Symbols[bb.B])}\tR{bb.A}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.GetSV:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"GETSV\t\tR{bb.A}\t{symbolTable.NameOf(irep.Symbols[bb.B])}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.SetSV:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"SETSV\t\t{symbolTable.NameOf(irep.Symbols[bb.B])}\tR{bb.A}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.GetConst:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"GETCONST\tR{bb.A}\t{symbolTable.NameOf(irep.Symbols[bb.B])}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.SetConst:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"SETCONST\t{symbolTable.NameOf(irep.Symbols[bb.B])}\tR{bb.A}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.GetMCnst:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"GETMCNST\tR{bb.A}\tR{bb.A}::{symbolTable.NameOf(irep.Symbols[bb.B])}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.SetMCnst:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"SETMCNST\tR{bb.A}::{symbolTable.NameOf(irep.Symbols[bb.B])}\tR{bb.A}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.GetIV:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"GETIV\t\tR{bb.A}\t{symbolTable.NameOf(irep.Symbols[bb.B])}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.SetIV:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"SETIV\t\t{symbolTable.NameOf(irep.Symbols[bb.B])}\tR{bb.A}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.GetUpVar:
                        var bbb = OperandBBB.Read(irep.Sequence, ref pc);
                    {
                        Format(writer, $"GETUPVAR\tR{bbb.A}\t{bbb.B}\t{bbb.C}\t");
                        WriteLocalVariableA(bbb.A);
                        break;
                    }
                    case OpCode.SetUpVar:
                        {
                            bbb = OperandBBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"SETUPVAR\tR{bbb.A}\t{bbb.B}\t{bbb.C}\t");
                            WriteLocalVariableA(bbb.A);
                            break;
                        }
                    case OpCode.GetCV:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"GETCV\t\tR{bb.A}\t{symbolTable.NameOf(irep.Symbols[bb.B])}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.SetCV:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"SETCV\t\t{symbolTable.NameOf(irep.Symbols[bb.B])}\tR{bb.A}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.GetIdx:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"GETIDX\tR{b.A}\tR{b.A + 1}\n");
                            break;
                        }
                    case OpCode.SetIdx:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"SETIDX\tR{b.A}\tR{b.A + 1}\tR{b.A + 2}\n");
                            break;
                        }
                    case OpCode.Jmp:
                        {
                            var s = OperandS.Read(irep.Sequence, ref pc);
                            var i = pc;
                            Format(writer, $"JMP\t\t{i + s.A}\n");
                            break;
                        }
                    case OpCode.JmpUw:
                        {
                            var s = OperandS.Read(irep.Sequence, ref pc);
                            var i = pc;
                            Format(writer, $"JMPUW\t\t{i + s.A}\n");
                            break;
                        }
                    case OpCode.JmpIf:
                        {
                            bs = OperandBS.Read(irep.Sequence, ref pc);
                            var i = pc;
                            Format(writer, $"JMPIF\t\tR{bs.A}\t{i + bs.B}\t");
                            WriteLocalVariableA(bs.A);
                            break;
                        }
                    case OpCode.JmpNot:
                        {
                            bs = OperandBS.Read(irep.Sequence, ref pc);
                            var i = pc;
                            Format(writer, $"JMPNOT\t\tR{bs.A}\t{i + bs.B}\t");
                            WriteLocalVariableA(bs.A);
                            break;
                        }
                    case OpCode.JmpNil:
                        {
                            bs = OperandBS.Read(irep.Sequence, ref pc);
                            var i = pc;
                            Format(writer, $"JMPNIL\tR{bs.A}\t{i + bs.B}\t");
                            WriteLocalVariableA(bs.A);
                            break;
                        }
                    case OpCode.SSend:
                        {
                            bbb = OperandBBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"SSEND\t\tR{bbb.A}\t:{symbolTable.NameOf(irep.Symbols[bbb.B])}\t");
                            WriteLocalVariableA(bbb.A);
                            break;
                        }
                    case OpCode.SSendB:
                        {
                            bbb = OperandBBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"SSENDB\tR{bbb.A}\t:{symbolTable.NameOf(irep.Symbols[bbb.B])}\t");
                            WriteLocalVariableA(bbb.A);
                            break;
                        }
                    case OpCode.Send:
                        {
                            bbb = OperandBBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"SEND\t\tR{bbb.A}\t:{symbolTable.NameOf(irep.Symbols[bbb.B])}\t");
                            WriteLocalVariableA(bbb.A);
                            break;
                        }
                    case OpCode.SendB:
                        {
                            bbb = OperandBBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"SENDB\t\tR{bbb.A}\t:{symbolTable.NameOf(irep.Symbols[bbb.B])}\t");
                            WriteLocalVariableA(bbb.A);
                            break;
                        }
                    case OpCode.Call:
                        {
                            OperandZ.Read(irep.Sequence, ref pc);
                            writer.Write("CALL\n"u8);
                            break;
                        }
                    case OpCode.Super:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"SUPER\t\tR{bb.A}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.ArgAry:
                        {
                            bs = OperandBS.Read(irep.Sequence, ref pc);
                            Format(writer, $"ARGARY\tR{bs.A}\t{(bs.B >> 11) & 0x3f}:{(bs.B >> 10) & 0x1}:{(bs.B >> 5) & 0x1f}:{(bs.B >> 4) & 0x1f} ({bs.B & 0xf})\t");
                            WriteLocalVariableA(bs.A);
                            break;
                        }
                    case OpCode.Enter:
                        {
                            var a = OperandW.Read(irep.Sequence, ref pc).A;
                            Format(writer, $"ENTER\t\t{(a >> 18) & 0x1f}:{(a >> 13) & 0x1f}:{(a >> 12) & 0x1}:{(a >> 7) & 0x1f}:{(a >> 2) & 0x1f}:{(a >> 1) & 0x1}:{a & 1} (0x{a:x})\n");
                            break;
                        }
                    case OpCode.KeyP:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"KEY_P\t\tR{bb.A}\t:{symbolTable.NameOf(irep.Symbols[bb.B])}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.KeyEnd:
                        {
                            OperandZ.Read(irep.Sequence, ref pc);
                            writer.Write("KEYEND\n"u8);
                            break;
                        }
                    case OpCode.KArg:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"KARG\t\tR{bb.A}\t:{symbolTable.NameOf(irep.Symbols[bb.B])}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.Return:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"RETURN\t\tR{b.A}\t\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.ReturnBlk:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"RETURN_BLK\tR{b.A}\t\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.Break:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"BREAK\t\tR{b.A}\t\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.BlkPush:
                        {
                            bs = OperandBS.Read(irep.Sequence, ref pc);
                            Format(writer, $"BLKPUSH\t\tR{bs.A}\t{(bs.B >> 11) & 0x3f}:{(bs.B >> 10) & 0x1}:{(bs.B >> 5) & 0x1f}:{(bs.B >> 4) & 0x1} ({(bs.B >> 0) & 0xf})\t");
                            WriteLocalVariableA(bs.A);
                            break;
                        }
                    case OpCode.Lambda:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"LAMBDA\t\tR{bb.A}\tI[{bb.B}]\n");
                            break;
                        }
                    case OpCode.Block:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"BLOCK\t\tR{bb.A}\tI[{bb.B}]\n");
                            break;
                        }
                    case OpCode.Method:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"METHOD\t\tR{bb.A}\tI[{bb.B}]\n");
                            break;
                        }
                    case OpCode.RangeInc:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"RANGE_INC\tR{b.A}\n");
                            break;
                        }
                    case OpCode.RangeExc:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"RANGE_EXC\tR{b.A}\n");
                            break;
                        }
                    case OpCode.Def:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"DEF\t\tR{bb.A}\t:{symbolTable.NameOf(irep.Symbols[bb.B])}\t(R{bb.A + 1})\n");
                            break;
                        }
                    case OpCode.Undef:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"UNDEF\t\t:{symbolTable.NameOf(irep.Symbols[b.A])}\n");
                            break;
                        }
                    case OpCode.Alias:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"ALIAS\t\t:{symbolTable.NameOf(irep.Symbols[bb.A])}\t:{symbolTable.NameOf(irep.Symbols[bb.B])}\n");
                            break;
                        }
                    case OpCode.Add:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"ADD\t\tR{b.A}\tR{b.A + 1}\n");
                            break;
                        }
                    case OpCode.AddI:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"ADDI\t\tR{bb.A}\t{bb.B}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.Sub:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"SUB\t\tR{b.A}\tR{b.A + 1}\n");
                            break;
                        }
                    case OpCode.SubI:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"SUBI\t\tR{bb.A}\t{bb.B}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.Mul:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"MUL\t\tR{b.A}\tR{b.A + 1}\n");
                            break;
                        }
                    case OpCode.Div:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"DIV\t\tR{b.A}\tR{b.A + 1}\n");
                            break;
                        }
                    case OpCode.LT:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"LT\t\tR{b.A}\tR{b.A + 1}\n");
                            break;
                        }
                    case OpCode.LE:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"LE\t\tR{b.A}\tR{b.A + 1}\n");
                            break;
                        }
                    case OpCode.GT:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"GT\t\tR{b.A}\tR{b.A + 1}\n");
                            break;
                        }
                    case OpCode.GE:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"GE\t\tR{b.A}\tR{b.A + 1}\n");
                            break;
                        }
                    case OpCode.EQ:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"EQ\t\tR{b.A}\tR{b.A + 1}\n");
                            break;
                        }
                    case OpCode.Array:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"ARRAY\t\tR{bb.A}\tR{bb.A}\t{bb.B}");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.Array2:
                        {
                            bbb = OperandBBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"ARRAY\t\tR{bbb.A}\tR{bbb.B}\t{bbb.C}");
                            WriteLocalVariableA(bbb.A);
                            break;
                        }
                    case OpCode.AryCat:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"ARYCAT\t\tR{b.A}\tR{b.A + 1}\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.AryPush:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"ARYPUSH\t\tR{bb.A}\t{bb.B}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.ArySplat:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"ARYSPLAT\tR{b.A}\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.ARef:
                        {
                            bbb = OperandBBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"AREF\t\tR{bbb.A}\tR{bbb.B}\t{bbb.C}");
                            WriteLocalVariableA(bbb.A);
                            break;
                        }
                    case OpCode.ASet:
                        {
                            bbb = OperandBBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"ASET\t\tR{bbb.A}\tR{bbb.B}\t{bbb.C}");
                            WriteLocalVariableAB(bbb.A, bbb.B);
                            break;
                        }
                    case OpCode.APost:
                        {
                            bbb = OperandBBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"APOST\t\tR{bbb.A}\t{bbb.B}\t{bbb.C}");
                            WriteLocalVariableA(bbb.A);
                            break;
                        }
                    case OpCode.Intern:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"INTERN\t\tR{b.A}\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.Symbol:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"SYMBOL\t\tR{bb.A}\tL[{bb.B}]\t; {irep.PoolValues[bb.B].As<RString>().AsSpan()}");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.String:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"STRING\t\tR{bb.A}\tL[{bb.B}]\t; {irep.PoolValues[bb.B].As<RString>().AsSpan()}");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.StrCat:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"STRCAT\t\tR{b.A}\tR{b.A + 1}\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.Hash:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"HASH\t\tR{bb.A}\t{bb.B}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.HashAdd:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"HASHADD\t\tR{bb.A}\t{bb.B}\t");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.HashCat:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"HASHCAT\t\tR{b.A}\tR{b.A + 1}\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.OClass:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"OCLASS\t\tR{b.A}\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.Class:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"CLASS\t\tR{bb.A}\t:{symbolTable.NameOf(irep.Symbols[bb.B])}");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.Module:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"MODULE\t\tR{bb.A}\t:{symbolTable.NameOf(irep.Symbols[bb.B])}");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.Exec:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"EXEC\t\tR{bb.A}\tI[{bb.B}]");
                            WriteLocalVariableA(bb.A);
                            break;
                        }
                    case OpCode.SClass:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"SCLASS\t\tR{b.A}\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.TClass:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"TCLASS\t\tR{b.A}\t\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.Err:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            var message = irep.PoolValues[b.A];
                            if (message.Object is RString)
                                Format(writer, $"ERR\t\t{message.As<RString>().AsSpan()}\n");
                            else Format(writer, $"ERR\t\tL[{b.A}]\n");
                            break;
                        }
                    case OpCode.Except:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"EXCEPT\t\tR{b.A}\t\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.Rescue:
                        {
                            bb = OperandBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"RESCUE\t\tR{bb.A}\tR{bb.B}");
                            WriteLocalVariableAB(bb.A, bb.B);
                            break;
                        }
                    case OpCode.RaiseIf:
                        {
                            b = OperandB.Read(irep.Sequence, ref pc);
                            Format(writer, $"RAISEIF\tR{b.A}\t\t");
                            WriteLocalVariableA(b.A);
                            break;
                        }
                    case OpCode.Debug:
                        {
                            bbb = OperandBBB.Read(irep.Sequence, ref pc);
                            Format(writer, $"DEBUG\t\t{bbb.A}\t{bbb.B}\t{bbb.C}\n");
                            break;
                        }
                    case OpCode.Stop:
                        {
                            OperandZ.Read(irep.Sequence, ref pc);
                            writer.Write("STOP\n"u8);
                            break;
                        }
                    case OpCode.EXT1:
                        {
                            OperandZ.Read(irep.Sequence, ref pc);
                            writer.Write("EXT1\n"u8);
                            break;
                        }
                    case OpCode.EXT2:
                        {
                            OperandZ.Read(irep.Sequence, ref pc);
                            writer.Write("EXT2\n"u8);
                            break;
                        }
                    case OpCode.EXT3:
                        {
                            OperandZ.Read(irep.Sequence, ref pc);
                            writer.Write("EXT3\n"u8);
                            break;
                        }
                }
            }

            writer.Write("\n"u8);
        }
    }
}