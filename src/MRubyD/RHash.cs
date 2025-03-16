using System.Collections;
using System.Runtime.CompilerServices;

namespace MRubyD;

public sealed class RHash : RObject, IEnumerable<KeyValuePair<MRubyValue, MRubyValue>>
{
    public int Length => table.Count;
    public IEnumerable<MRubyValue> Keys => table.Keys;
    public IEnumerable<MRubyValue> Values => table.Values;

    readonly Dictionary<MRubyValue, MRubyValue> table;

    internal RHash(int capacity, IEqualityComparer<MRubyValue> comparer, RClass hashClass) : base(MRubyVType.Hash, hashClass)
    {
        table = new Dictionary<MRubyValue, MRubyValue>(capacity, comparer);
    }

    public MRubyValue this[MRubyValue key]
    {
        get => table.TryGetValue(key, out var value) ? value : MRubyValue.Nil;
        set => table[key] = value;
    }

    RHash(Dictionary<MRubyValue, MRubyValue> table, RClass hashClass) : base(MRubyVType.Hash, hashClass)
    {
        this.table = table;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(MRubyValue key, MRubyValue value)
    {
        table.Add(key, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(MRubyValue key, MRubyValue value)
    {
        table[key] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(MRubyValue key, out MRubyValue value)
    {
        return table.TryGetValue(key, out value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue Delete(MRubyValue key)
    {
        table.Remove(key, out var value);
        return value;
    }

    public RHash Dup() => new(table, Class);

    public void Merge(RHash other)
    {
        if (this == other) return;
        if (other.Length == 0) return;

        foreach (var x in other.table)
        {
            this[x.Key] = x.Value;
        }
    }

    public Dictionary<MRubyValue, MRubyValue>.Enumerator GetEnumerator() => table.GetEnumerator();

    IEnumerator<KeyValuePair<MRubyValue, MRubyValue>> IEnumerable<KeyValuePair<MRubyValue, MRubyValue>>.GetEnumerator() =>
        table.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
