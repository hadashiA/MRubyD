namespace MRubyD.StdLib;

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

    // [MRubyMethod(RequiredArguments = 1)]
    // public static MRubyMethod IsInclude = new((state, self) =>
    // {
    //     var range = self.As<RRange>();
    //     var rangeOther = self.As<RRange>();
    //
    //     if (range.Begin.IsNil)
    //     {
    //         if (range.Exclusive)
    //     }
    // });
}
