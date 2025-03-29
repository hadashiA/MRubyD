namespace MRubyCS.StdLib;

public class HashMembers
{
    public static MRubyMethod ToS = new((state, self) =>
    {
        var hash = self.As<RHash>();
        var result = state.NewString("{"u8);
        if (state.IsRecursiveCalling(hash, Names.Inspect))
        {
            result.Concat("...}"u8);
        }
        else
        {
            var first = true;
            foreach (var (key, value) in hash)
            {
                if (!first)
                {
                    result.Concat(", "u8);
                }
                first = false;

                if (key.IsSymbol)
                {
                    var keyString = state.NameOf(key.SymbolValue);
                    result.Concat(keyString);
                    result.Concat(": "u8);
                }
                else
                {
                    var keyString = state.Stringify(state.Send(key, Names.Inspect));
                    result.Concat(keyString);
                    result.Concat(" => "u8);
                }
                var valueString = state.Stringify(state.Send(value, Names.Inspect));
                result.Concat(valueString);
            }
            result.Concat("}"u8);
        }
        return MRubyValue.From(result);
    });


    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpEq = new((state, self) =>
    {
        var hash = self.As<RHash>();
        var arg = state.GetArg(0);
        if (arg.Object is not RHash other || hash.Length != other.Length)
        {
            return MRubyValue.False;
        }

        if (hash == other)
        {
            return MRubyValue.True;
        }

        foreach (var (key, value) in hash)
        {
            if (other.TryGetValue(key, out var otherValue))
            {
                var valueEquals = state.Send(value, Names.OpEq, otherValue);
                if (valueEquals.Falsy) return MRubyValue.False;
            }
            else
            {
                return MRubyValue.False;
            }
        }
        return MRubyValue.True;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Eql = new((state, self) =>
    {
        var hash = self.As<RHash>();
        var arg = state.GetArg(0);
        if (arg.Object is not RHash other || hash.Length != other.Length)
        {
            return MRubyValue.False;
        }

        if (hash == other)
        {
            return MRubyValue.True;
        }

        foreach (var (key, value) in hash)
        {
            if (other.TryGetValue(key, out var otherValue))
            {
                var valueEquals = state.Send(value, Names.QEql, otherValue);
                if (valueEquals.Falsy) return MRubyValue.False;
            }
            else
            {
                return MRubyValue.False;
            }
        }
        return MRubyValue.True;
   });
}