namespace MRubyD.StdLib;

static class NumericMembers
{
    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Eql = new(((state, self) =>
    {
        var other = state.GetArg(0);
        if (self.IsFloat)
        {
            if (!other.IsFloat) return MRubyValue.False;
            return MRubyValue.From(self.FloatValue == other.FloatValue);
        }

        if (self.IsInteger)
        {
            if (!other.IsInteger) return MRubyValue.False;
            return MRubyValue.From(self.IntegerValue == other.IntegerValue);
        }

        return MRubyValue.From(self == other);
    }));
}