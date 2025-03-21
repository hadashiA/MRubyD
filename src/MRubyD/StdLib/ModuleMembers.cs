using System;

namespace MRubyD.StdLib;

static class ModuleMembers
{
    [MRubyMethod]
    public static MRubyMethod Initialize = new((state, self) =>
    {
        var mod = self.As<RClass>();
        var block = state.GetBlockArg();
        if (block.Object is RProc proc)
        {
            state.YieldWithClass(mod, self, [self], proc);
        }
        return self;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod ExtendObject = new((state, self) =>
    {
        var obj = state.GetArg(0);
        state.EnsureValueType(obj, MRubyVType.Module);

        var target = state.SingletonClassOf(self);
        if (target is null)
        {
            state.Raise(Names.TypeError, "can't define singleton"u8);
        }

        state.IncludeModule(target!, self.As<RClass>());
        return self;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod PrependFeatures = new((state, self) =>
    {
        state.EnsureValueType(self, MRubyVType.Module);
        var c = state.GetArg(0);
        state.PrependModule(c.As<RClass>(), self.As<RClass>());
        return self;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod AppendFeatures = new((state, self) =>
    {
        state.EnsureValueType(self, MRubyVType.Module);
        var c = state.GetArg(0);
        state.IncludeModule(c.As<RClass>(), self.As<RClass>());
        return self;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod QInclude = new((state, self) =>
    {
        var c = self.As<RClass>();
        var mod2 = state.GetArg(0);
        state.EnsureValueType(mod2, MRubyVType.Module);

        while (c != null!)
        {
            if (c.VType == MRubyVType.IClass && c.Class == mod2.As<RClass>())
            {
                return MRubyValue.True;
            }

            c = c.Super;
        }

        return MRubyValue.False;
    });

    [MRubyMethod(RestArguments = true)]
    public static MRubyMethod ClassEval = new((state, self) =>
    {
        throw new NotImplementedException();
    });

    [MRubyMethod(RestArguments = true)]
    public static MRubyMethod ModuleFunction = new((state, self) =>
    {
        state.EnsureValueType(self, MRubyVType.Module);

        state.Raise(Names.NotImplementedError, "not implemented"u8);
        return MRubyValue.Nil;
    });

    [MRubyMethod(RestArguments = true)]
    public static MRubyMethod AttrReader = new((state, self) =>
    {
        var mod = self.As<RClass>();
        var argv = state.GetRestArg(0);
        foreach (var arg in argv)
        {
            var methodId = arg.SymbolValue;
            var name = state.PrepareInstanceVariableName(methodId);

            state.DefineMethod(mod, methodId, (s, _) =>
            {
                var runtimeSelf = s.GetSelf();
                return runtimeSelf.As<RObject>().InstanceVariables.Get(name);
            });
        }
        return MRubyValue.Nil;
    });

    [MRubyMethod(RestArguments = true)]
    public static MRubyMethod AttrWriter = new((state, self) =>
    {
        var mod = self.As<RClass>();
        var argv = state.GetRestArg(0);
        foreach (var arg in argv)
        {
            var variableName = state.PrepareInstanceVariableName(arg.SymbolValue);
            var setterName = state.PrepareName(arg.SymbolValue, default, "="u8);

            state.DefineMethod(mod, setterName, new MRubyMethod((s, _) =>
            {
                var value = s.GetArg(0);
                mod.InstanceVariables.Set(variableName, value);
                return MRubyValue.Nil;
            }));
        }
        return MRubyValue.Nil;
    });

    [MRubyMethod(RestArguments = true)]
    public static MRubyMethod AttrAccessor = new((state, mod) =>
    {
        AttrReader.Invoke(state, mod);
        return AttrWriter.Invoke(state, mod);
    });

    [MRubyMethod]
    public static MRubyMethod ToS = new((state, self) =>
    {
        var mod = self.As<RClass>();
        if (mod.VType == MRubyVType.SClass)
        {
            var v = mod.InstanceVariables.Get(Names.AttachedKey);
            return MRubyValue.From(v.VType.IsClass()
                ? state.NewString($"<Class:{state.Inspect(v)}>")
                : state.NewString($"<Class:{state.StringifyAny(v)}>"));
        }

        return MRubyValue.From(state.NameOf(mod));
    });

    [MRubyMethod(RequiredArguments = 2)]
    public static MRubyMethod AliasMethod = new((state, self) =>
    {
        var mod = self.As<RClass>();
        var newName = state.GetArg(0).SymbolValue;
        var oldName = state.GetArg(1).SymbolValue;
        state.AliasMethod(mod, newName, oldName);
        state.MethodAddedHook(mod, newName);
        return self;
    });

    [MRubyMethod(RestArguments = true)]
    public static MRubyMethod UndefMethod = new((state, self) =>
    {
        var c = self.As<RClass>();
        var argv = state.GetRestArg(0);
        foreach (var arg in argv)
        {
            state.UndefMethod(c, arg.SymbolValue);
        }

        return self;
    });

    [MRubyMethod]
    public static MRubyMethod Ancestors = new((state, self) =>
    {
        var c = self.As<RClass>();
        var result = state.NewArray();

        while (c != null!)
        {
            if (c.VType == MRubyVType.IClass)
            {
                result.Push(MRubyValue.From(c.Class));
            }
            else if (!c.Flags.HasFlag(MRubyObjectFlags.ClassPrepended))
            {
                result.Push(MRubyValue.From(c));
            }

            c = c.Super;
        }

        return MRubyValue.From(result);
    });

    [MRubyMethod(RequiredArguments = 1, OptionalArguments = 1)]
    public static MRubyMethod ConstDefined = new((state, self) =>
    {
        var mod = self.As<RClass>();
        var id = state.GetArgAsSymbol(0);
        var inherit = state.GetArg(1);
        state.EnsureConstName(id);
        var result = inherit.Truthy
            ? state.ConstDefinedAt(id, mod)
            : state.ConstDefinedAt(id, mod, true);
        return MRubyValue.From(result);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod ConstGet = new((state, self) =>
    {
        var mod = self.As<RClass>();
        var path = state.GetArg(0);
        if (path.IsSymbol)
        {
            state.TryGetConst(path.SymbolValue, mod, out var x);
            return x;
        }

        // const get with class path string
        var pathString = path.As<RString>().AsSpan();
        var result = MRubyValue.Nil;
        while (true)
        {
            var end = pathString.IndexOf("::"u8);
            if (end < 0) end = pathString.Length;
            var id = state.Intern(pathString[..end]);
            if (!state.TryGetConst(id, mod, out result))
            {
                state.RaiseNameError(id, state.NewString($"wrong constant name '{pathString}'"));
            }

            if (end == pathString.Length)
            {
                break;
            }

            mod = result.As<RClass>();
            pathString = pathString[(end + 2)..];
        }

        return result;
    });

    [MRubyMethod(RequiredArguments = 2)]
    public static MRubyMethod ConstSet = new((state, self) =>
    {
        var mod = self.As<RClass>();
        var id = state.GetArg(0).SymbolValue;
        var value = state.GetArg(1);
        state.DefineConst(mod, id, value);
        return value;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod RemoveConst = new((state, self) =>
    {
        var n = state.GetArg(0).SymbolValue;
        state.EnsureConstName(n);
        var removed = state.RemoveInstanceVariable(self, n);
        if (removed.IsUndef)
        {
            state.RaiseNameError(n, state.NewString($"constant {n} is not defined"));
        }

        return removed;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod ConstMissing = new((state, self) =>
    {
        var name = state.GetArg(0).SymbolValue;
        state.RaiseConstMissing(self.As<RClass>(), name);
        return MRubyValue.Nil;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod MethodDefined = new((state, self) =>
    {
        var methodId = state.GetArg(0).SymbolValue;
        return MRubyValue.From(state.RespondTo(self, methodId));
    });

    [MRubyMethod(RequiredArguments = 1, OptionalArguments = 1, BlockArgument = true)]
    public static MRubyMethod DefineMethod = new((state, self) =>
    {
        var methodId = state.GetArg(0).SymbolValue;
        var proc = state.GetArg(1);
        var block = state.GetBlockArg();

        if (proc.IsNil) proc = MRubyValue.Undef;
        if (proc is { IsUndef: false, IsProc: false })
        {
            state.Raise(
                Names.ArgumentError,
                state.NewString($"wrong argument type {state.Stringify(proc)} (expected Proc)"));
        }
        if (block.IsNil)
        {
            state.Raise(Names.ArgumentError, "no block given"u8);
        }

        var p = block.As<RProc>().Clone();
        p.SetFlag(MRubyObjectFlags.ProcStrict);
        var method = new MRubyMethod((RProc)p);

        var mod = self.As<RClass>();
        state.DefineMethod(mod, methodId, method);
        state.MethodAddedHook(mod, methodId);

        return MRubyValue.From(methodId);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Eqq = new((state, self) =>
    {
        var mod = self.As<RClass>();
        var other = state.GetArg(0);
        return MRubyValue.From(state.KindOf(other, mod));
    });

    [MRubyMethod]
    public static MRubyMethod Dup = new((state, self) =>
    {
        var clone = state.CloneObject(self);
        if (clone.Object is { } obj)
        {
            obj.UnFreeze();
        }
        return clone;
    });
}
