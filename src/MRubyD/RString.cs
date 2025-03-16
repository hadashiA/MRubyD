using System.Runtime.CompilerServices;
using System.Text;

namespace MRubyD;

enum RStringRangeType
{
    /// <summary>
    /// `beg` and `len` are byte unit in `0 ... str.bytesize`
    /// </summary>
    ByteRangeCorrected = 1,

    /// <summary>
    /// `beg` and `len` are char unit in any range
    /// </summary>
    CharRange = 2,

    /// <summary>
    /// `beg` and `len` are char unit in `0 ... str.size`
    /// </summary>
    CharRangeCorrected = 3,

    /// <summary>
    /// `beg` is out of range
    /// </summary>
    OutOfRange = -1
}

public class RString : RObject, ISpanFormattable, IUtf8SpanFormattable, IEquatable<RString>
{
    public int Length { get; internal set; }

    byte[] buffer;
    bool bufferOwned;

    public static RString Owned(byte[] value, RClass stringClass)
    {
        return new RString(value, value.Length, stringClass);
    }

    public static RString Owned(byte[] value, int length, RClass stringClass)
    {
        return new RString(value, length, stringClass);
    }

    public static RString operator+(RString a, RString b)
    {
        var buffer = new byte[a.Length + b.Length];
        a.AsSpan().CopyTo(buffer);
        b.AsSpan().CopyTo(buffer.AsSpan(a.Length));
        return Owned(buffer, a.Class);
    }

    internal RString(int capacity, RClass stringClass)
        : base(MRubyVType.String, stringClass)
    {
        buffer = new byte[capacity];
        Length = 0;
        bufferOwned = true;
    }

    internal RString(ReadOnlySpan<byte> utf8, RClass stringClass)
        : base(MRubyVType.String, stringClass)
    {
        buffer = new byte[utf8.Length];
        Length = utf8.Length;
        bufferOwned = true;
        utf8.CopyTo(buffer);
    }

    RString(RString shared) : base(MRubyVType.String, shared.Class)
    {
        buffer = shared.buffer;
        Length = shared.Length;
        bufferOwned = false;
    }

    RString(byte[] buffer, int length, RClass stringClass) : base(MRubyVType.String, stringClass)
    {
        this.buffer = buffer;
        Length = length;
        bufferOwned = true;
        MarkAsFrozen();
    }

    public static implicit operator Span<byte>(RString str) => str.AsSpan();
    public static implicit operator ReadOnlySpan<byte>(RString str) => str.AsSpan();

    public static bool operator ==(RString? left, RString? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null)
            return false;
        return left.Equals(right);
    }

    public static bool operator !=(RString? left, RString? right)
    {
        return !(left == right);
    }

    public override string ToString()
    {
        return Encoding.UTF8.GetString(buffer, 0, Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> AsSpan() => buffer.AsSpan(0, Length);

    public RString Dup() => new(this);

    public RString SubSequence(int start, int length)
    {
        return new RString(AsSpan().Slice(start, length), Class);
    }

    public RString SubString(int start, int length)
    {
        // TODO:
        var str = ToString();
        var substr = str.Substring(start, length);
        var utf8Substr = Encoding.UTF8.GetBytes(substr);
        return new RString(utf8Substr, Class);
    }

    public RString? GetAref(MRubyValue indexValue, int rangeLength = -1)
    {
        switch (CalculateStringRange(indexValue, rangeLength, out var calculatedOffset, out var calculatedLength))
        {
            case RStringRangeType.ByteRangeCorrected:
            {
                if (indexValue.Object is RString str)
                {
                    return str.Dup();
                }
                return SubSequence(calculatedOffset, calculatedLength);
            }
            case RStringRangeType.CharRange:
                return SubString(calculatedOffset, calculatedLength);

            case RStringRangeType.CharRangeCorrected:
            {
                if (indexValue.Object is RString str)
                {
                    return str.Dup();
                }
                return SubString(calculatedOffset, calculatedLength);
            }
            default:
                return null;
        }
    }

    public void Concat(RString other)
    {
        Concat(other.AsSpan());
    }

    public void Concat(ReadOnlySpan<byte> utf8)
    {
        var newLength = Length + utf8.Length;
        if (bufferOwned)
        {
            if (buffer.Length < newLength)
            {
                Array.Resize(ref buffer, newLength);
            }
        }
        else
        {
            var newBuffer = new byte[newLength];
            AsSpan().CopyTo(newBuffer);
            buffer = newBuffer;
            bufferOwned = true;
        }
        utf8.CopyTo(buffer.AsSpan(Length));
        Length = newLength;
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        FormattableString formattable =
            $"{nameof(buffer)}: {buffer}, {nameof(Length)}: {Length}, {nameof(bufferOwned)}: {bufferOwned}";
        return formattable.ToString(formatProvider);
    }

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        return destination.TryWrite(provider,
            $"{nameof(buffer)}: {buffer}, {nameof(Length)}: {Length}, {nameof(bufferOwned)}: {bufferOwned}",
            out charsWritten);
    }

    public bool TryFormat(
        Span<byte> destination,
        out int bytesWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        var span = AsSpan();
        if (destination.Length < span.Length)
        {
            bytesWritten = default;
            return false;
        }
        span.CopyTo(destination);
        bytesWritten = span.Length;
        return true;
    }

    public int IndexOf(ReadOnlySpan<byte> str, int offset = 0)
    {
        if (Length - offset < str.Length)
        {
            return -1;
        }
        if (offset > 0)
        {
            str = str[..offset];
        }
        return AsSpan().IndexOf(str);
    }

    RStringRangeType CalculateStringRange(
        MRubyValue index,
        int indexLength,
        out int calculatedOffset,
        out int calculatedLength)
    {
        if (indexLength >= 0)
        {
            calculatedOffset = (int)index.IntegerValue;
            calculatedLength = indexLength;
            return RStringRangeType.CharRange;
        }

        if (index.IsInteger)
        {
            calculatedOffset = (int)index.IntegerValue;
            calculatedLength = 1;
            return RStringRangeType.CharRange;
        }

        switch (index.Object)
        {
            case RString str:
                calculatedOffset = IndexOf(str);
                calculatedLength = str.Length;
                return calculatedOffset < 0
                    ? RStringRangeType.OutOfRange
                    : RStringRangeType.ByteRangeCorrected;
            case RRange range:
                if (range.Calculate(Length, true, out calculatedOffset, out calculatedLength) == RangeCalculateResult.Ok)
                {
                    return RStringRangeType.CharRangeCorrected;
                }
                break;
        }

        calculatedOffset = default;
        calculatedLength = default;
        return RStringRangeType.OutOfRange;
    }

    public override int GetHashCode()
    {
        const uint OffsetBasis = 2166136261u;
        const uint FnvPrime = 16777619u;
        var hash = OffsetBasis;
        foreach (var b in AsSpan())
        {
            hash ^= b;
            hash *= FnvPrime;
        }
        return unchecked((int)hash);
    }

    public bool Equals(RString? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return AsSpan().SequenceEqual(other.AsSpan());
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((RString)obj);
    }
}
