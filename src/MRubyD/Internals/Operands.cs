using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MRubyD.Internals;

internal enum OperandType
{
    Z,
    B,
    S,
    BB,
    BS,
    BBB,
    BSS,
}

[StructLayout(LayoutKind.Explicit)]
internal readonly struct BigEndianInt16
{
    [FieldOffset(0)]
    public readonly byte A;

    [FieldOffset(1)]
    public readonly byte B;

    public short Value => unchecked((short)((A << 8) | B));
}

[StructLayout(LayoutKind.Explicit)]
internal struct Operand
{
    [FieldOffset(0)]
    public OperandType Type;

    [FieldOffset(1)]
    public OperandB B;

    [FieldOffset(1)]
    public OperandS S;

    [FieldOffset(1)]
    public OperandBB BB;

    [FieldOffset(1)]
    public OperandBS BS;

    [FieldOffset(1)]
    public OperandBBB BBB;

    [FieldOffset(1)]
    public OperandBSS BSS;
}

[StructLayout(LayoutKind.Explicit)]
internal struct OperandZ
{
    public static void Read(ReadOnlySpan<byte> sequence, ref int pc)
    {
        pc += 1;
    }
}

[StructLayout(LayoutKind.Explicit)]
internal struct OperandB
{
    [FieldOffset(0)]
    public byte A;

    public static OperandB Read(ReadOnlySpan<byte> sequence, ref int pc)
    {
        var result = Unsafe.ReadUnaligned<OperandB>(ref Unsafe.Add(ref MemoryMarshal.GetReference(sequence), (1 + pc)));
        pc += 2;
        return result;
    }
}

[StructLayout(LayoutKind.Explicit)]
internal struct OperandBB
{
    [FieldOffset(0)]
    public byte A;

    [FieldOffset(1)]
    public byte B;

    public static OperandBB Read(ReadOnlySpan<byte> sequence, ref int pc)
    {
        var result = Unsafe.ReadUnaligned<OperandBB>(ref Unsafe.Add(ref MemoryMarshal.GetReference(sequence), (1 + pc)));
        pc += 3;
        return result;
    }
}

[StructLayout(LayoutKind.Explicit)]
internal struct OperandS
{
    [FieldOffset(0)]
    public short A;

    [FieldOffset(1)]
    BigEndianInt16 bigEndianA;


    public static OperandS Read(ReadOnlySpan<byte> sequence, ref int pc)
    {
        var result = Unsafe.ReadUnaligned<OperandS>(ref Unsafe.Add(ref MemoryMarshal.GetReference(sequence), (1 + pc)));
        pc += 3;
        result.A = result.bigEndianA.Value;
        return result;
    }
}

[StructLayout(LayoutKind.Explicit)]
internal struct OperandBS
{
    [FieldOffset(0)]
    public byte A;

    [FieldOffset(1)]
    public short B;

    [FieldOffset(1)]
    BigEndianInt16 bigEndianB;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OperandBS Read(ReadOnlySpan<byte> sequence, ref int pc)
    {
        var result = Unsafe.ReadUnaligned<OperandBS>(ref Unsafe.Add(ref MemoryMarshal.GetReference(sequence), (1 + pc)));
        pc += 4;
        result.B = result.bigEndianB.Value;
        return result;
    }
}

[StructLayout(LayoutKind.Explicit)]
internal struct OperandBBB
{
    [FieldOffset(0)]
    public byte A;

    [FieldOffset(1)]
    public byte B;

    [FieldOffset(2)]
    public byte C;

    public static OperandBBB Read(ReadOnlySpan<byte> sequence, ref int pc)
    {
        var result = Unsafe.ReadUnaligned<OperandBBB>(ref Unsafe.Add(ref MemoryMarshal.GetReference(sequence), (1 + pc)));
        pc += 4;
        return result;
    }
}

[StructLayout(LayoutKind.Explicit)]
internal struct OperandBSS
{
    [FieldOffset(0)]
    public byte A;

    [FieldOffset(1)]
    public short B;

    [FieldOffset(3)]
    public short C;

    [FieldOffset(1)]
    BigEndianInt16 bigEndianB;

    [FieldOffset(3)]
    BigEndianInt16 bigEndianC;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OperandBSS Read(ReadOnlySpan<byte> sequence, ref int pc)
    {
        var result = Unsafe.ReadUnaligned<OperandBSS>(ref Unsafe.Add(ref MemoryMarshal.GetReference(sequence), (1 + pc)));
        pc += 6;
        result.B = result.bigEndianB.Value;
        result.C = result.bigEndianC.Value;
        return result;
    }
}