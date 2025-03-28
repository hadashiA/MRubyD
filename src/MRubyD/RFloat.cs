using MRubyD.Internals;

namespace MRubyD;

public sealed class RFloat : RObject
{
    public double Value { get; }

    internal RFloat(double value, RClass floatClass) : base(InternalMRubyType.Float, floatClass)
    {
        Value = value;
    }
}
