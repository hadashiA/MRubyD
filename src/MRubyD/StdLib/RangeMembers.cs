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
}
