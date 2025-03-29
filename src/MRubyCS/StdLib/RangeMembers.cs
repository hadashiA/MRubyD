using System.Runtime.CompilerServices;

namespace MRubyCS.StdLib;

static class RangeMembers
{
    public static MRubyMethod Begin = new((state, self) =>
    {
        return self.As<RRange>().Begin;
    });

    public static MRubyMethod End = new((state, self) =>
    {
        return self.As<RRange>().End;
    });

    public static MRubyMethod ExcludeEnd = new((state, self) =>
    {
        return MRubyValue.From(self.As<RRange>().Exclusive);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpEq = new((state, self) =>
    {
        var arg0 = state.GetArg(0);
        if (self == arg0) return MRubyValue.True;

        var range = self.As<RRange>();
        if (arg0.Object is not RRange rangeOther)
        {
            return MRubyValue.False;
        }
        return MRubyValue.From(range.Begin == rangeOther.Begin &&
                               range.End == rangeOther.End &&
                               range.Exclusive == rangeOther.Exclusive);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod IsInclude = new((state, self) =>
    {
        var range = self.As<RRange>();
        var value = state.GetArg(0);

        if (range.Begin.IsNil)
        {
            var result = range.Exclusive
                // end > value
                ? state.ValueCompare(range.End, value) == 1
                // end >= value
                : state.ValueCompare(range.End, value) is 0 or 1;
            return MRubyValue.From(result);
        }

        // begin <= value
        if (state.ValueCompare(range.Begin, value) is 0 or -1)
        {
            if (range.End.IsNil)
            {
                return MRubyValue.True;
            }

            var result = range.Exclusive
                // end > value
                ? state.ValueCompare(range.End, value) == 1
                // end >= value
                : state.ValueCompare(range.End, value) is 0 or 1;
            return MRubyValue.From(result);
        }
        return MRubyValue.False;
    });

    public static MRubyMethod ToS = new((state, self) =>
    {
        var range = self.As<RRange>();
        var b = state.Stringify(range.Begin);
        var e = state.Stringify(range.End);

        var result = range.Exclusive
            ? state.NewString($"{b}...{e}")
            : state.NewString($"{b}..{e}");
        return MRubyValue.From(result);
    });

    public static MRubyMethod Inspect = new((state, self) =>
    {
        var range = self.As<RRange>();
        var result = state.NewString(6);
        if (!range.Begin.IsNil)
        {
            var b = state.InspectObject(range.Begin);
            result.Concat(b);
        }
        result.Concat(range.Exclusive ? "..."u8 : ".."u8);
        if (!range.End.IsNil)
        {
            var e = state.InspectObject(range.End);
            result.Concat(e);
        }
        return MRubyValue.From(result);
    });
}
