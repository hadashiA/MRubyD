namespace MRubyCS.StdLib;

static class ExceptionMembers
{
    [MRubyMethod(RestArguments = true, BlockArgument = true)]
    public static MRubyMethod New = new((state, self) =>
    {
        var args = state.GetRestArg(0);
        var block = state.GetBlockArg();

        var c = self.As<RClass>();
        var o = new RException(null, c);
        var value = MRubyValue.From(o);
        if (state.TryFindMethod(c, Names.Initialize, out var method, out _) &&
            method != MRubyMethod.Nop)
        {
            state.Send(value, Names.Initialize, args, kargs: null, block: block.IsNil ? null : block.As<RProc>());
        }
        return value;
    });

    [MRubyMethod(OptionalArguments =  1)]
    public static MRubyMethod Exception = new((state, self) =>
    {
        if (!state.TryGetArg(0, out var arg) || arg == self)
        {
            return self;
        }

        var ex = state.CloneObject(self);
        ex.As<RException>().Message = state.Stringify(arg);
        return ex;
    });

    [MRubyMethod(OptionalArguments =  1)]
    public static MRubyMethod Initialize = new((state, self) =>
    {
        if (state.TryGetArg(0, out var arg))
        {
            self.As<RException>().Message = state.Stringify(arg);
        }
        return self;
    });

    [MRubyMethod]
    public static MRubyMethod ToS = new((state, self) =>
    {
        if (self.As<RException>().Message is { } message)
        {
            return MRubyValue.From(message);
        }
        return MRubyValue.From(state.NameOf(state.ClassOf(self)));
    });

    [MRubyMethod]
    public static MRubyMethod Inspect = new((state, self) =>
    {
        var className = state.NameOf(state.ClassOf(self));
        var message = self.As<RException>().Message;
        if (message is { Length: > 0 })
        {
            return MRubyValue.From(state.NewString($"{message} ({className})"));
        }
        return MRubyValue.From(className);
    });

    [MRubyMethod]
    public static MRubyMethod Backtrace = new((state, self) =>
    {
        var backtrace = self.As<RException>().Backtrace;
        if (backtrace is null)
        {
            return MRubyValue.Nil;
        }
        return MRubyValue.From(backtrace.ToRArray(state));
    });
}
