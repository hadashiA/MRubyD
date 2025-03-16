namespace MRubyD;

public sealed class RInteger : RObject
{
    public nint Value { get; }

    internal RInteger(nint value, RClass integerClass) : base(MRubyVType.Integer, integerClass)
    {
        Value = value;
    }
}
