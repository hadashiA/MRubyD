namespace MRubyD.StdLib;

static class FalseClassMembers
{
    static readonly byte[] FalseString = "false"u8.ToArray();

    [MRubyMethod]
    public static MRubyMethod And = new((state, self) => MRubyValue.False);

    [MRubyMethod]
    public static MRubyMethod Or = new((state, self) =>
    {
        return MRubyValue.From(state.GetArg(0).Truthy);
    });

    [MRubyMethod]
    public static MRubyMethod Xor = new((state, self) =>
    {
        return MRubyValue.From(state.GetArg(0).Truthy);
    });

    public static MRubyMethod ToS = new((state, self) =>
    {
        var result = state.NewStringOwned(FalseString);
        result.MarkAsFrozen();
        return MRubyValue.From(result);
    });
}
