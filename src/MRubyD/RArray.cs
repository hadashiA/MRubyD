using System.Runtime.CompilerServices;

namespace MRubyD;

public sealed class RArray : RObject
{
    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private set;
    }

    MRubyValue[] data;
    int offset;
    bool dataOwned;

    public Span<MRubyValue> AsSpan() => data.AsSpan(offset, Length);

    public Span<MRubyValue> AsSpan(int start, int count) =>
        data.AsSpan(offset + start, count);

    public Span<MRubyValue> AsSpan(int start) =>
        data.AsSpan(offset + start, Length - start);

    internal RArray(ReadOnlySpan<MRubyValue> values, RClass arrayClass)
        : base(MRubyVType.Array, arrayClass)
    {
        Length = values.Length;
        offset = 0;
        data = values.ToArray();
        dataOwned = true;
    }

    internal RArray(int capacity, RClass arrayClass) : base(MRubyVType.Array, arrayClass)
    {
        Length = 0;
        offset = 0;
        data = new MRubyValue[capacity];
        dataOwned = true;
    }

    RArray(RArray shared)
        : this(shared, 0, shared.Length)
    {
    }

    RArray(RArray shared, int offset, int size) : base(MRubyVType.Array, shared.Class)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (size > shared.Length)
        {
            size = shared.Length;
        }
        Length = size;
        this.offset = offset;
        data = shared.data;
        dataOwned = false;
    }

    internal override RObject Clone()
    {
        var clone = new RArray(data.Length, Class);
        InstanceVariables.CopyTo(clone.InstanceVariables);
        return clone;
    }

    public MRubyValue this[int index]
    {
        get
        {
            if ((uint)index < (uint)Length)
            {
                return data[index];
            }
            return MRubyValue.Nil;
        }
        set
        {
            EnsureModifiable(index + 1, true);
            data[index] = value;
        }
    }

    public override string ToString()
    {
        var list = AsSpan().ToArray().Select(x => x.ToString());
        return $"[{string.Join(", ", list)}]";
    }

    public RArray Dup() => new(this);

    public RArray SubSequence(int start, int length)
    {
        return new RArray(this, start, length);
    }

    public void Push(MRubyValue newItem)
    {
        EnsureModifiable(Length + 1, true);
        data[Length] = newItem;
    }

    public void Unshift(MRubyValue newItem)
    {
        var src = AsSpan();
        if (data.Length <= Length)
        {
            data = new MRubyValue[Length * 2];
        }

        dataOwned = true;
        var dst = data.AsSpan(1, Length);
        src.CopyTo(dst);
        dst[0] = newItem;
        Length++;
    }

    public void Concat(RArray other)
    {
        if (Length <= 0)
        {
            Length = other.Length;
            data = other.data;
            dataOwned = false;
            return;
        }

        var start = Length;
        var newLength = start + other.Length;
        EnsureModifiable(newLength, true);
        other.AsSpan().CopyTo(data.AsSpan(start));
    }

    public void CopyTo(RArray other)
    {
        if (other.Length < Length)
        {
            other.EnsureModifiable(Length);
        }
        AsSpan().CopyTo(other.AsSpan());
        other.Length = Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void EnsureModifiable(int capacity, bool expandLength = false)
    {
        if (data.Length < capacity)
        {
            var newLength = data.Length * 2;
            if (newLength < capacity)
            {
                newLength = capacity;
            }

            if (dataOwned)
            {
                Array.Resize(ref data, newLength);
            }
            else
            {
                var newData = new MRubyValue[newLength];
                data.CopyTo(newData, 0);
                data = newData;
                dataOwned = true;
            }
        }
        else if (!dataOwned)
        {
            data = data.ToArray();
            dataOwned = true;
        }

        if (expandLength)
        {
            Length = capacity;
        }
    }
}
