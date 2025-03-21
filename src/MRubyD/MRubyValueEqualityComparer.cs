using System.Collections.Generic;

namespace MRubyD;

public class MRubyValueEqualityComparer(MRubyState state) : IEqualityComparer<MRubyValue>
{
    public bool Equals(MRubyValue x, MRubyValue y)
    {
        return state.ValueEquals(x, y);
    }

    public int GetHashCode(MRubyValue value)
    {
        return value.GetHashCode();
    }
}