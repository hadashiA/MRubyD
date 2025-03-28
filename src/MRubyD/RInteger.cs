using MRubyD.Internals;

namespace MRubyD;

public sealed class RInteger : RObject
{
    public nint Value { get; }

    internal RInteger(nint value, RClass integerClass) : base(InternalMRubyType.Integer, integerClass)
    {
        Value = value;
    }
}
