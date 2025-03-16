namespace MRubyD.StdLib;

static class TrueClassMembers
{
    static readonly byte[] TrueString = "true"u8.ToArray();

    public static MRubyMethod And = new((state, self) =>
    {
        return MRubyValue.From(state.GetArg(0).Truthy);
    });

    public static MRubyMethod Or = new((state, self) =>
    {
        return MRubyValue.True;
    });

    public static MRubyMethod Xor = new((state, self) =>
    {
        return MRubyValue.From(!state.GetArg(0).Truthy);
    });

    public static MRubyMethod ToS = new((state, self) =>
    {
        var result = state.NewStringOwned(TrueString);
        result.MarkAsFrozen();
        return MRubyValue.From(result);
    });
}
