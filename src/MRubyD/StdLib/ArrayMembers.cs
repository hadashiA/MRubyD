namespace MRubyD.StdLib;

static class ArrayMembers
{
    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Initialize = new((state, self) =>
    {
        var array = self.As<RArray>();
        var arg0 = state.GetArg(0);
        var arg1 = state.GetArg(1);
        var block = state.GetBlockArg();

        if (arg0.Object is RArray src && arg1.IsNil && block.IsNil)
        {
            src.CopyTo(array);
            return self;
        }

        var size = state.ToInteger(arg0);
        array.EnsureModifiable((int)size, true);
        var span = array.AsSpan();
        for (var i = 0; i < size; i++)
        {
            if (block.Object is RProc proc)
            {
                var procSelf = state.GetProcSelf(proc, out var targetClass);
                span[i] = state.YieldWithClass(targetClass, procSelf, [MRubyValue.From(i)], proc);
            }
            else
            {
                span[i] = arg1;
            }
        }
        return self;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Push = new((state, self) =>
    {
        var array = self.As<RArray>();
        var args = state.GetRestArg(0);

        var start = array.Length;
        array.EnsureModifiable(start + args.Length, true);

        var span = array.AsSpan(start, args.Length);
        foreach (var t in args)
        {
            span[0] = t;
        }
        return self;
    });

    [MRubyMethod]
    public static MRubyMethod Size = new((state, self) =>
    {
        var array = self.As<RArray>();
        return MRubyValue.From(array.Length);
    });

    [MRubyMethod]
    public static MRubyMethod Empty = new((state, self) =>
    {
        var array = self.As<RArray>();
        return MRubyValue.From(array.Length <= 0);
    });

    [MRubyMethod(OptionalArguments = 1)]
    public static MRubyMethod First = new((state, self) =>
    {
        var array = self.As<RArray>();
        if (state.GetArgumentCount() <= 0)
        {
            return array.Length <= 0 ? MRubyValue.Nil : array[0];
        }

        var size = state.GetArgAsInteger(0);
        var subSequence = array.SubSequence(0, (int)size);
        return MRubyValue.From(subSequence);
    });


    [MRubyMethod(OptionalArguments = 1)]
    public static MRubyMethod Last = new((state, self) =>
    {
        var array = self.As<RArray>();
        if (state.GetArgumentCount() <= 0)
        {
            return array.Length <= 0 ? MRubyValue.Nil : array[^1];
        }

        var size = state.GetArgAsInteger(0);
        var subSequence = array.SubSequence(array.Length - (int)size, (int)size);
        return MRubyValue.From(subSequence);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpEq = new((state, self) =>
    {
        var array = self.As<RArray>();
        var arg = state.GetArg(0);
        if (arg.Object is not RArray other ||
            array.Length != other.Length)
        {
            return MRubyValue.False;
        }

        if (array == other)
        {
            return MRubyValue.True;
        }

        var span1 = array.AsSpan();
        var span2 = other.AsSpan();
        for (var i = 0; i < span1.Length; i++)
        {
            var elementEquals = state.Send(span1[i], Names.OpEq, span2[i]);
            if (elementEquals.Falsy)
            {
                return MRubyValue.False;
            }
        }
        return MRubyValue.True;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Eql = new((state, self) =>
    {
        var array = self.As<RArray>();
        var arg = state.GetArg(0);
        if (arg.Object is not RArray other ||
            array.Length != other.Length)
        {
            return MRubyValue.False;
        }

        if (array == other)
        {
            return MRubyValue.True;
        }

        var span1 = array.AsSpan();
        var span2 = other.AsSpan();
        for (var i = 0; i < span1.Length; i++)
        {
            var elementEquals = state.Send(span1[i], Names.QEql, span2[i]);
            if (elementEquals.Falsy)
            {
                return MRubyValue.False;
            }
        }
        return MRubyValue.True;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpAdd = new((state, self) =>
    {
        var array = self.As<RArray>();
        var other = state.GetArg(0);
        state.EnsureValueType(other, MRubyVType.Array);

        var otherArray = other.As<RArray>();

        var newLength = array.Length + otherArray.Length;
        var newArray = state.NewArray(newLength);
        newArray.EnsureModifiable(newLength, true);

        var span = newArray.AsSpan();
        array.AsSpan().CopyTo(span);
        otherArray.AsSpan().CopyTo(span[array.Length..]);
        return MRubyValue.From(newArray);
    });

    public static MRubyMethod ReverseBang = new((state, self) =>
    {
        var array = self.As<RArray>();
        var span = array.AsSpan();

        var left = 0;
        var right = span.Length - 1;
        while (left < right)
        {
            (span[left], span[right]) = (span[right], span[left]);
            left++;
            right--;
        }
        return self;
    });

    public static MRubyMethod ToS = new((state, self) =>
    {
        var array = self.As<RArray>();
        var result = state.NewString("["u8);
        if (state.IsRecursiveCalling(array, Names.Inspect))
        {
            result.Concat("...]"u8);
        }
        else
        {
            var first = true;
            foreach (var x in array.AsSpan())
            {
                if (!first)
                {
                    result.Concat(", "u8);
                }
                first = false;

                var value = state.Stringify(state.Send(x, Names.Inspect));
                result.Concat(value);
            }
            result.Concat("]"u8);
        }
        return MRubyValue.From(result);
    });

    // [MRubyMethod(OptionalArguments = 1)]
    // public static MRubyMethod Join = new((state, self) =>
    // {
    //     RString? separator = null;
    //     if (state.TryGetArg(0, out var arg0))
    //     {
    //         state.EnsureValueType(arg0, MRubyVType.String);
    //         separator = arg0.As<RString>();
    //     }
    //
    //     var array = self.As<RArray>();
    //     var span = array.AsSpan();
    //
    //     // check recursive
    //     foreach (var x in span)
    //     {
    //         if (x == self)
    //         {
    //             state.Raise(Names.ArgumentError, "recursive array join"u8);
    //         }
    //     }
    //
    //     var result = state.NewString(array.Length * 2);
    //     var first = true;
    //     foreach (var x in span)
    //     {
    //         if (!first && separator != null)
    //         {
    //             result.Concat(separator);
    //         }
    //         first = false;
    //
    //         if (x.Object is RString str)
    //         {
    //             result.Concat(str);
    //         }
    //         else if (x.Object is RArray arr)
    //         {
    //             state.Send(x, state.Intern("join"u8));
    //             result.Concat(Join(state, x).As<RString>());
    //         }
    //         switch (x.VType)
    //         {
    //             case MRubyVType.String:
    //                 result.Concat(x.As<RString>());
    //                 break;
    //             case MRubyVType.Array:
    //                 re
    //                 break;
    //         }
    //     }
    // });

    // internal method to convert multi-value to single value
    public static MRubyMethod SValue = new((state, self) =>
    {
        var array = self.As<RArray>();
        return array.Length switch
        {
            0 => MRubyValue.Nil,
            1 => array[0],
            _ => self
        };
    });
}