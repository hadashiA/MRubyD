using System.Buffers;
using System.Runtime.CompilerServices;

namespace MRubyD.Internals;

static class NamingRule
{
    static readonly byte[] DigitTable = "0123456789abcdefghijklmnopqrstuvwxyz"u8.ToArray();

    [ThreadStatic]
    static ArrayBufferWriter<byte>? bufferWriterStatic;

    public static bool IsConstName(ReadOnlySpan<byte> name)
    {
        return name.Length > 0 && AsciiCode.IsUpper(name[0]) &&
               (name.Length == 1 || AsciiCode.IsIdentifier(name[..^1]));
    }

    public static bool IsInstanceVariableName(ReadOnlySpan<byte> name)
    {
        if (name.Length < 2) return false;

        if (name[0] != '@') return false;
        if (AsciiCode.IsDigit(name[1])) return false;
        return AsciiCode.IsIdentifier(name);
    }

    //
    public static bool IsSymbolName(ReadOnlySpan<byte> name)
    {
        if (name.Length <= 0) return false;

        switch (name[0])
        {
            case (byte)'\0':
                return false;

            case (byte)'$':
                if (name.Length == 1) return false;
                return IsSpecialGlobalName(name[1..]) || IsIdentifier(name[1..], false);

            case (byte)'@':
                if (name.Length == 1) return false;
                if (name[1] == (byte)'@')
                {
                    return name.Length > 2 && IsIdentifier(name[2..], false);
                }
                return IsIdentifier(name[1..], false);

            case (byte)'<':
                return name.Length == 1 ||
                       (name.Length == 2 && name[1] is (byte)'<' or (byte)'=') ||
                       (name.Length == 3 && name[1] is (byte)'=' && name[2] is (byte)'>');

            case (byte)'>':
                return name.Length == 1 ||
                       (name.Length == 2 && name[1] is (byte)'>' or (byte)'=');

            case (byte)'=':
                return name.Length == 1 ||
                       (name.Length == 2 && name[1] is (byte)'~' or (byte)'=') ||
                       (name.Length == 3 && name[1] is (byte)'=' && name[2] is (byte)'=');

            case (byte)'*':
                return name.Length < 2 ||
                       (name.Length < 3 && name[1] == (byte)'*');

            case (byte)'!':
                return name.Length == 1 ||
                       (name.Length == 2 && name[1] is (byte)'=' or (byte)'~');

            case (byte)'+':
            case (byte)'-':
                return name.Length == 1 |
                       (name.Length == 2 && name[1] is (byte)'@');

            case (byte)'|':
                return name.Length == 1 ||
                       (name.Length == 2 && name[1] is (byte)'|');

            case (byte)'&':
                return name.Length == 1 ||
                       (name.Length == 2 && name[1] is (byte)'&');

            case (byte)'^':
            case (byte)'/':
            case (byte)'%':
            case (byte)'~':
            case (byte)'`':
                return name.Length == 1;

            case (byte)'[':
                return name.Length == 3 &&
                       name[1] is (byte)']' &&
                       name[2] is (byte)'=';

            default:
                return IsIdentifier(name, !AsciiCode.IsUpper(name[0]));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsIdentifier(ReadOnlySpan<byte> name, bool localId)
        {
            if (name.Length <= 0) return false;
            if (name[0] != '_' && !AsciiCode.IsAlphabet(name[0]))
            {
                return false;
            }

            var i = 0;
            while (i < name.Length && AsciiCode.IsDigit(name[i])) i++;
            if (localId)
            {
                if (name[i] is (byte)'!' or (byte)'?' or (byte)'=')
                {
                    i++;
                }
            }
            return i >= name.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsSpecialGlobalName(ReadOnlySpan<byte> name)
        {
            switch (name[0])
            {
                case (byte)'_':
                case (byte)'*':
                case (byte)'$':
                case (byte)'?':
                case (byte)'!':
                case (byte)'@':
                case (byte)'/':
                case (byte)'\\':
                case (byte)';':
                case (byte)',':
                case (byte)'.':
                case (byte)'=':
                case (byte)':':
                case (byte)'<':
                case (byte)'>':
                case (byte)'"':
                case (byte)'&':
                case (byte)'`':
                case (byte)'\'':
                case (byte)'+':
                case (byte)'0':
                    return name.Length == 1;
                case (byte)'-':
                    return name.Length < 2 ||
                           AsciiCode.IsIdentifier(name[1]);
                default:
                    foreach (var b in name)
                    {
                        if (!AsciiCode.IsDigit(b)) return false;
                    }
                    return true;
            }
        }
    }

    public static bool TryEscape(ReadOnlySpan<byte> name, bool inspect, Span<byte> output, out int written)
    {
        var offset = 0;
        written = 0;
        output[offset++] = (byte)'"';

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '"' || c == '\\' || (c == '#' && IsEvStr(i + 1, name)))
            {
                if (output.Length < offset + 2) return false;
                output[offset++] = (byte)'\\';
                output[offset++] = c;
                continue;
            }

            if (AsciiCode.IsPrint(c))
            {
                if (output.Length < offset + 1) return false;
                output[offset++] = c;
                continue;
            }

            if (output.Length < offset + 4) return false;
            output[offset++] = (byte)'\\';
            switch (c)
            {
                case (byte)'\n':
                    output[offset++] = (byte)'n';
                    break;
                case (byte)'\r':
                    output[offset++] = (byte)'r';
                    break;
                case (byte)'\t':
                    output[offset++] = (byte)'t';
                    break;
                case (byte)'\f':
                    output[offset++] = (byte)'f';
                    break;
                case 11: // '013'
                    output[offset++] = (byte)'v';
                    break;
                case 8: // 010
                    output[offset++] = (byte)'b';
                    break;
                case 7: // 007
                    output[offset++] = (byte)'a';
                    break;
                case 033: // 007
                    output[offset++] = (byte)'e';
                    break;
                default: // hex encode
                    var lower = DigitTable[c / 16 % 16];
                    var upper = DigitTable[c % 16];
                    output[offset++] = (byte)'x';
                    output[offset++] = upper;
                    output[offset++] = lower;
                    break;
            }
        }
        output[offset++] = (byte)'"';
        written = offset;
        return true;

        bool IsEvStr(int i, ReadOnlySpan<byte> name)
        {
            if (i >= name.Length) return false;
            var c = name[i];
            return c == '$' || c == '@' || c == '{';
        }
    }
}