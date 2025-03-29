using System;

namespace MRubyCS.StdLib;

static class FloatMembers
{
    public static MRubyMethod ToI = new((state, self) =>
    {
        var f = self.FloatValue;
        state.EnsureFloatValue(f);
        if (!IsFixableFloatValue(f))
        {
            state.Raise(Names.RangeError, state.NewString($"integer overflow in to_f"));
        }

        if (f > 0.0) return MRubyValue.From(Math.Floor(f));
        if (f < 0.0) return MRubyValue.From(Math.Ceiling(f));
        return MRubyValue.From((long)f);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Mod = new((state, self) =>
    {
        var x = self.FloatValue;
        var y = state.GetArgAsFloat(0);
        if (double.IsNaN(y))
        {
            return MRubyValue.From(double.NaN);
        }

        if (y == 0.0)
        {
            state.Raise(Names.ZeroDivisionError, "divided by 0"u8);
        }

        if (double.IsInfinity(y) && !double.IsInfinity(x))
        {
            return MRubyValue.From(x);
        }
        return MRubyValue.From(x % y);
    });

    static bool IsFixableFloatValue(double f) =>
        f is >= -9223372036854775808.0 and < 9223372036854775808.0;

}