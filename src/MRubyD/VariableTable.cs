using System.Collections;
using System.Runtime.CompilerServices;

namespace MRubyD;

public class VariableTable : IEnumerable<KeyValuePair<Symbol, MRubyValue>>
{
    readonly Dictionary<Symbol, MRubyValue> values = new();

    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => values.Count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Defined(Symbol id) => values.ContainsKey(id);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet(Symbol id, out MRubyValue value) => values.TryGetValue(id, out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue Get(Symbol id)
    {
        if (TryGet(id, out var result))
        {
            return result;
        }
        return MRubyValue.Nil;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(Symbol id, MRubyValue value)
    {
        values[id] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Remove(Symbol id, out MRubyValue removedValue)
    {
        return values.Remove(id, out removedValue);
    }

    public void Clear() => values.Clear();

    public void CopyTo(VariableTable other)
    {
        foreach (var (key, value) in values)
        {
            other.values.Add(key, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Dictionary<Symbol, MRubyValue>.Enumerator GetEnumerator() => values.GetEnumerator();

    IEnumerator<KeyValuePair<Symbol, MRubyValue>> IEnumerable<KeyValuePair<Symbol, MRubyValue>>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
