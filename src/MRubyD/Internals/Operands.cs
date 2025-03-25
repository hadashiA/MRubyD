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
    W
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
    [FieldOffset(1)]
    public OperandW W;
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
        pc += 2;
        var result = Unsafe.ReadUnaligned<OperandB>(ref Unsafe.Add(ref MemoryMarshal.GetReference(sequence), (pc - 1)));

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
        pc += 3;
        var result = Unsafe.ReadUnaligned<OperandBB>(ref Unsafe.Add(ref MemoryMarshal.GetReference(sequence), (pc - 2)));

        return result;
    }
}

[StructLayout(LayoutKind.Explicit)]
internal unsafe struct OperandS
{
    [FieldOffset(0)]
    public short A;

    [FieldOffset(0)]
    fixed byte bytesA[2];


    public static OperandS Read(ReadOnlySpan<byte> sequence, ref int pc)
    {
        pc += 3;
        var result = Unsafe.ReadUnaligned<OperandS>(ref Unsafe.Add(ref MemoryMarshal.GetReference(sequence), (pc - 2)));
        result.A = (short)((result.bytesA[0] << 8) | result.bytesA[1]);
        return result;
    }
}

[StructLayout(LayoutKind.Explicit)]
internal unsafe struct OperandBS
{
    [FieldOffset(0)]
    public byte A;

    [FieldOffset(1)]
    public short B;

    [FieldOffset(1)]
    fixed byte bytesB[2];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OperandBS Read(ReadOnlySpan<byte> sequence, ref int pc)
    {
        pc += 4;
        var result = Unsafe.ReadUnaligned<OperandBS>(ref Unsafe.Add(ref MemoryMarshal.GetReference(sequence), (pc - 3)));
        result.B = (short)((result.bytesB[0] << 8) | result.bytesB[1]);
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
        pc += 4;
        var result = Unsafe.ReadUnaligned<OperandBBB>(ref Unsafe.Add(ref MemoryMarshal.GetReference(sequence), (pc - 3)));
        return result;
    }
}

[StructLayout(LayoutKind.Explicit)]
internal unsafe struct OperandBSS
{
    [FieldOffset(0)]
    public byte A;

    [FieldOffset(1)]
    public short B;

    [FieldOffset(3)]
    public short C;

    [FieldOffset(1)]
    fixed byte bytesB[2];

    [FieldOffset(3)]
    fixed byte bytesC[2];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OperandBSS Read(ReadOnlySpan<byte> sequence, ref int pc)
    {
        pc += 6;
        var result = Unsafe.ReadUnaligned<OperandBSS>(ref Unsafe.Add(ref MemoryMarshal.GetReference(sequence), (pc - 5)));

        result.B = (short)((result.bytesB[0] << 8) | result.bytesB[1]);
        result.C = (short)((result.bytesC[0] << 8) | result.bytesC[1]);
        return result;
    }
}

internal unsafe struct OperandW
{
    // ReSharper disable once UnassignedField.Local
    public fixed byte Bytes[3];
    public int A => (Bytes[0] << 16) | (Bytes[1] << 8) | Bytes[2];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OperandW Read(ReadOnlySpan<byte> sequence, ref int pc)
    {
        pc += 4;
        var result = Unsafe.ReadUnaligned<OperandW>(ref Unsafe.Add(ref MemoryMarshal.GetReference(sequence), (pc - 3)));
        return result;
    }
}