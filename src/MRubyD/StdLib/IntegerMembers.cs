using System;

namespace MRubyD.StdLib;

static class IntegerMembers
{
    public static MRubyMethod ToS = new((state, self) =>
    {
        var basis = 10;
        if (state.GetArgumentCount() > 0)
        {
            basis = (int)state.GetArgAsInteger(0);
        }

        return MRubyValue.From(state.StringifyInteger(self, basis));
    });

    public static MRubyMethod OpPlus = new((state, self) =>
    {
        return MRubyValue.From(+self.IntegerValue);
    });

    public static MRubyMethod OpMinus = new((state, self) =>
    {
        return MRubyValue.From(-self.IntegerValue);
    });

    public static MRubyMethod Abs = new((state, self) =>
    {
        return MRubyValue.From(Math.Abs(self.IntegerValue));
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Mod = new((state, self) =>
    {
        var a = state.ToInteger(self);
        if (a == 0) return self;

        var other = state.GetArg(0);
        if (other.IsInteger)
        {
            var b = other.IntegerValue;
            if (b == 0)
            {
                state.Raise(Names.ZeroDivisionError, "divided by 0"u8);
            }

            var mod = a % b;
            if ((a < 0) != (b < 0) && mod != 0)
            {
                mod += b;
            }
            return MRubyValue.From(mod);
        }
        return FloatMembers.Mod.Invoke(state, self);
    });
}
