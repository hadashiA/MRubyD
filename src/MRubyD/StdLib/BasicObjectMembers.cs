using System;

namespace MRubyD.StdLib;

static class BasicObjectMembers
{
    [MRubyMethod]
    public static MRubyMethod Not = new((state, self) => MRubyValue.From(!self.BoolValue));

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpEq = new((state, self) =>
    {
        return MRubyValue.From(self == state.GetArg(0));
    });

    public static MRubyMethod Id = new((state, self) =>
    {
        return MRubyValue.From(self.ObjectId);
    });

    public static MRubyMethod Send = new((state, self) =>
    {
        return state.SendMeta(self);
    });

    public static MRubyMethod InstanceEval = new((state, self) =>
    {
        throw new NotImplementedException();
    });

    public static MRubyMethod MethodMissing = new((state, self) =>
    {
        var methodId = state.GetArgAsSymbol(0);
        var args = state.GetRestArg(1);
        var array = MRubyValue.From(state.NewArray(args));
        state.RaiseMethodMissing(methodId, self, array);
        return MRubyValue.Nil;
    });
}
