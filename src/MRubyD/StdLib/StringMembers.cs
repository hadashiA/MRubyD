using System;
using System.Buffers;
using System.Buffers.Text;
using MRubyD.Internals;

namespace MRubyD.StdLib;

static class StringMembers
{
    public static MRubyMethod Inspect = new((state, self) =>
    {
        var str = self.As<RString>();
        var output = ArrayPool<byte>.Shared.Rent(str.Length * 2 + 2);

        int written;
        while (!NamingRule.TryEscape(str.AsSpan(), true, output, out written))
        {
            ArrayPool<byte>.Shared.Return(output);
            output = ArrayPool<byte>.Shared.Rent(output.Length * 2);
        }
        ArrayPool<byte>.Shared.Return(output);

        return MRubyValue.From(state.NewString(output.AsSpan(0, written)));
    });

    public static MRubyMethod OpEq = new((state, self) =>
    {
        var other = state.GetArg(0);
        if (other.Object is RString otherString)
        {
            return MRubyValue.From(self.As<RString>().Equals(otherString));
        }
        return MRubyValue.False;
    });

    public static MRubyMethod ToSym = new((state, self) =>
    {
        var str = self.As<RString>();
        var sym = state.Intern(str.AsSpan());
        return MRubyValue.From(sym);
    });

    public static MRubyMethod ToI = new((state, self) =>
    {
        var str = self.As<RString>();

        var format = 'g';
        if (state.TryGetArg(0, out var arg0))
        {
            var basis = state.ToInteger(arg0);
            switch (basis)
            {
                case 2:
                    format = 'b';
                    break;
                case 8:
                    format = 'o';
                    break;
                case 16:
                    format = 'x';
                    break;
                case 10:
                    format = 'g';
                    break;
                default:
                    state.Raise(Names.ArgumentError, state.NewString($"invalid radix {basis}"));
                    format = default;
                    break;
            }
        }

        Utf8Parser.TryParse(str.AsSpan(), out int result, out var consumed, format);
        return MRubyValue.From(result);
    });
}