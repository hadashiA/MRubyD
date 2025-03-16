namespace MRubyD.StdLib;

static class NilClassMembers
{
    [MRubyMethod]
    public static MRubyMethod Tos = new((state, self) =>
    {
        var result = state.NewString(0);
        result.MarkAsFrozen();
        return MRubyValue.From(result);
    });

    [MRubyMethod]
    public static MRubyMethod Inspect = new((state, self) =>
    {
        var result = state.NewString("nil"u8);
        result.MarkAsFrozen();
        return MRubyValue.From(result);
    });
}