using MRubyD.Internals;

namespace MRubyD.StdLib;

static class SymbolMembers
{
    public static MRubyMethod ToS = new((state, self) =>
    {
        var name = state.NameOf(self.SymbolValue);
        return MRubyValue.From(state.NewString(name.AsSpan()));
    });

    public static MRubyMethod Name = new((state, self) =>
    {
        return MRubyValue.From(state.NameOf(self.SymbolValue));
    });

    public static MRubyMethod Inspect = new((state, self) =>
    {
        var name = state.NameOf(self.SymbolValue);
        if (NamingRule.IsSymbolName(name))
        {
            Span<byte> buffer = stackalloc byte[name.Length + 1];
            buffer[0] = (byte)':';
            name.AsSpan().CopyTo(buffer[1..]);
            return MRubyValue.From(state.NewString(buffer));
        }

        Span<byte> escapeBuffer = stackalloc byte[name.Length * 2 + 4];
        escapeBuffer[0] = (byte)':';
        int escapedSize;
        while (!NamingRule.TryEscape(name, true, escapeBuffer[1..], out escapedSize))
        {
#pragma warning disable CA2014
            // ReSharper disable once StackAllocInsideLoop
            escapeBuffer = stackalloc byte[escapeBuffer.Length * 2];
#pragma warning restore CA2014
        }
        return MRubyValue.From(state.NewString(escapeBuffer[..(escapedSize + 1)]));
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Cmp = new((state, self) =>
    {
        var other = state.GetArg(0);
        if (!other.IsSymbol) return MRubyValue.Nil;

        var sym1 = self.SymbolValue;
        var sym2 = other.SymbolValue;
        if (sym1 == sym2)
        {
            return MRubyValue.From(0);
        }

        var str1 = state.NameOf(sym1);
        var str2 = state.NameOf(sym2);
        var result = str1.AsSpan().SequenceCompareTo(str2.AsSpan());
        return MRubyValue.From(result);
    });


}