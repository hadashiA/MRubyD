using System.Runtime.CompilerServices;

namespace MRubyD.Internals;

static class AsciiCode
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDigit(byte c) => (c | 0x20) - (byte)'0' < 10;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAlphabet(byte c) => (c | 0x20) - (byte)'a' < 26;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAscii(byte c) => c <= 0x7f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsUpper(byte c) => c - (byte)'A' < 26;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsLower(byte c) => c - (byte)'a' < 26;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsBlank(byte c) => c is (byte)' ' or (byte)'\t';

    public static bool IsIdentifier(byte c) => IsAlphabet(c) ||
                                               IsDigit(c) ||
                                               c == (byte)'_' ||
                                               !IsAscii(c);

    public static bool IsIdentifier(ReadOnlySpan<byte> span)
    {
        foreach (var x in span)
        {
            if (!IsIdentifier(x)) return false;
        }
        return true;
    }

    public static bool IsPrint(byte c) => (byte)(c - 0x20) < 0x5f;
}