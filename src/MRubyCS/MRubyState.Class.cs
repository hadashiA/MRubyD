using System;

namespace MRubyCS;

public sealed class ClassDefineOptions(MRubyState state, RClass c)
{
    public void DefineMethod(Symbol id, MRubyMethod method) => state.DefineMethod(c, id, method);
    public void DefineMethod(Symbol id, MRubyFunc func) => state.DefineMethod(c, id, func);

    public void DefineClassMethod(Symbol id, MRubyMethod method) => state.DefineClassMethod(c, id, method);
    public void DefineClassMethod(Symbol id, MRubyFunc func) => state.DefineClassMethod(c, id, func);
}

partial class MRubyState
{
    const int MethodCacheSize = 1 << 8; // TODO:

    public RClass SingletonClassOf(MRubyValue value)
    {
        switch (value.VType)
        {
            case MRubyVType.Nil:
                return NilClass;
            case MRubyVType.False:
                return FalseClass;
            case MRubyVType.True:
                return TrueClass;
            case MRubyVType.Symbol:
            case MRubyVType.Integer:
            case MRubyVType.Float:
                Raise(Names.TypeError, "can't define singleton"u8);
                return null!; // not reached
            default:
                var obj = value.As<RObject>();
                if (obj.Class == null!)
                {
                    Raise(Names.TypeError, "can't define singleton"u8);
                    return null!; // not reached
                }
                PrepareSingletonClass(obj);
                return obj.Class;
        }
    }

    public void IncludeModule(RClass c, RClass mod)
    {
        EnsureNotFrozen(c);
        if (!c.TryIncludeModule(c.AsOrigin(), mod, true))
        {
            Raise(Names.ArgumentError, "cyclic include detected"u8);
        }

        // TODO: https://github.com/mruby/mruby/commit/3972df57fe70a29e6bf6db590dd22651640a1217
        ClearMethodCache();
    }

    public void PrependModule(RClass c, RClass mod)
    {
        EnsureNotFrozen(c);
        if (!c.HasFlag(MRubyObjectFlags.ClassPrepended))
        {
            var origin = new RClass(ClassClass, MRubyVType.IClass)
            {
                Super = c.Super,
                InstanceVType = c.InstanceVType,
            };
            c.MoveMethodTableTo(origin);
            c.SetFlag(MRubyObjectFlags.ClassPrepended);
            origin.SetFlag(MRubyObjectFlags.ClassOrigin);
        }

        if (!c.TryIncludeModule(c, mod, false))
        {
            Raise(Names.ArgumentError, "cyclic prepend detected"u8);
        }

        // TODO: https://github.com/mruby/mruby/commit/3972df57fe70a29e6bf6db590dd22651640a1217

        ClearMethodCache();
    }

    public void DefineConst(RClass c, Symbol name, MRubyValue value)
    {
        EnsureConstName(name);
        EnsureValueIsConst(value);
        if (value.IsNamespace)
        {
            TrySetClassPathLink(c, value.As<RClass>(), name);
        }
        c.InstanceVariables.Set(name, value);
    }

    public RClass DefineClass(Symbol name, RClass super, MRubyVType? instanceVType = null, RClass? outer = null)
    {
        outer ??= ObjectClass;

        RClass c;
        if (super.VType != MRubyVType.Class)
        {
            Raise(Names.TypeError, NewString($"super class must be a Class ({super.VType} given)"));
        }

        if (TryGetConst(name, outer, out var value))
        {
            EnsureInheritable(super);
            EnsureValueType(value, MRubyVType.Class);

            c = value.As<RClass>();
            if (c.Super.GetRealClass() != super)
            {
                Raise(Names.TypeError, NewString(
                    $"superclass mismatch for Class {NameOf(name)} ({StringifyModule(c.Super)} not {StringifyModule(super)})"));
            }
            return c;
        }

        c = new RClass(ClassClass)
        {
            Super = super,
            InstanceVType = instanceVType ?? super.InstanceVType
        };
        TrySetClassPathLink(outer, c, name);
        PrepareSingletonClass(c);
        return c;
    }

    public RClass DefineClass(Symbol name, RClass super, Action<ClassDefineOptions> configure)
    {
        return DefineClass(name, super, null!, configure);
    }

    public RClass DefineClass(Symbol name, RClass super, RClass outer, Action<ClassDefineOptions> configure)
    {
        var c = DefineClass(name, super, outer: outer);
        configure(new ClassDefineOptions(this, c));
        return c;
    }

    public RClass DefineModule(Symbol name, RClass outer)
    {
        if (TryGetConst(name, outer, out var value))
        {
            EnsureValueType(value, MRubyVType.Module);
            return value.As<RClass>();
        }

        var m = new RClass(ModuleClass, MRubyVType.Module)
        {
            InstanceVType = MRubyVType.Undef,
            Super = null!
        };
        TrySetClassPathLink(outer, m, name);
        return m;
    }

    public void DefineMethod(RClass c, Symbol id, MRubyFunc func) =>
        DefineMethod(c, id, new MRubyMethod(func));


    public void DefineMethod(RClass c, Symbol id, MRubyMethod method)
    {
        c = c.AsOrigin();
        if (c is { IsSingletonClass: true, IsFrozen: true })
        {
            var instance = c.InstanceVariables.Get(Names.AttachedKey);
            EnsureNotFrozen(instance);
        }
        else
        {
            EnsureNotFrozen(c);
        }

        if (method.Proc is { } proc)
        {
            proc.SetFlag(MRubyObjectFlags.ProcScope);
            if (proc.Scope is not REnv)
            {
                proc.UpdateScope(c);
            }
        }
        c.MethodTable[id] = method;
    }

    public void AliasMethod(RClass c, Symbol aliasMethodId, Symbol methodId)
    {
        if (aliasMethodId == methodId) return;
        TryFindMethod(c, methodId, out var method, out _);
        DefineMethod(c, aliasMethodId, method);
    }

    public void UndefMethod(RClass c, Symbol methodId)
    {
        if (!TryFindMethod(c, methodId, out _, out _))
        {
            Raise(Names.NameError, NewString($"undefined method '{NameOf(methodId)}' for class '{StringifyModule(c)}'"));
        }
        DefineMethod(c, methodId, MRubyMethod.Nop);
    }

    public void DefineClassMethod(RClass c, Symbol methodId, MRubyMethod method)
    {
        DefineSingletonMethod(c, methodId, method);
    }

    public void DefineClassMethod(RClass c, Symbol methodId, MRubyFunc func)
    {
        DefineSingletonMethod(c, methodId, new MRubyMethod(func));
    }

    public void UndefClassMethod(RClass c, Symbol methodId)
    {
        UndefMethod(SingletonClassOf(MRubyValue.From(c))!, methodId);
    }

    public bool RespondTo(MRubyValue self, Symbol methodId)
    {
        return RespondTo(ClassOf(self), methodId);
    }

    public bool RespondTo(RClass c, Symbol methodId)
    {
        return TryFindMethod(c, methodId, out var method, out _) && method != MRubyMethod.Nop;
    }

    public bool TryFindMethod(RClass c, Symbol methodId, out MRubyMethod method, out RClass imp)
    {
        // TODO caching ?
        return c.TryFindMethod(methodId, out method, out imp);
    }

    void ClearMethodCache()
    {
    }

    void PrepareSingletonClass(RObject obj)
    {
        if (obj.Class.VType == MRubyVType.SClass) return;

        RClass singletonClass;
        if (obj is RClass { VType: MRubyVType.Class } objAsClass)
        {
            singletonClass = new RClass(ClassClass, MRubyVType.SClass)
            {
                Super = objAsClass.Super == ObjectClass || objAsClass.Super == null!
                    ? ClassClass
                    : objAsClass.Super.Class,
                InstanceVType = MRubyVType.Undef,
            };
        }
        else if (obj is RClass { VType: MRubyVType.SClass } objAsSClass)
        {
            var c = objAsSClass;
            while (c.Super.VType == MRubyVType.IClass)
            {
                c = c.Super;
            }
            PrepareSingletonClass(c.Super);
            singletonClass = new RClass(ClassClass, MRubyVType.SClass)
            {
                Super = c.Super.Class,
                InstanceVType = MRubyVType.Undef,
            };
        }
        else
        {
            singletonClass = new RClass(ClassClass, MRubyVType.SClass)
            {
                Super = obj.Class,
                InstanceVType = MRubyVType.Undef,
            };
            PrepareSingletonClass(singletonClass);
        }
        singletonClass.InstanceVariables.Set(Names.AttachedKey, MRubyValue.From(obj));
        obj.Class = singletonClass;
    }

    public void ClassInheritedHook(RClass superClass, RClass newClass)
    {
        superClass.SetFlag(MRubyObjectFlags.ClassInherited);
        if (RespondTo(superClass.Class, Names.Inherited))
        {
            Send(MRubyValue.From(superClass), Names.Inherited, MRubyValue.From(newClass));
        }
    }

    public void MethodAddedHook(RClass c, Symbol methodId)
    {
        Symbol added;
        if (c.VType == MRubyVType.SClass)
        {
            added = Names.SingletonMethodAdded;
            var receiver = c.InstanceVariables.Get(Names.AttachedKey);
            if (RespondTo(receiver, added))
            {
                Send(receiver, added, MRubyValue.From(methodId));
            }
        }
        else
        {
            added = Names.MethodAdded;
            if (RespondTo(c, added))
            {
                var receiver = MRubyValue.From(c);
                Send(receiver, added, MRubyValue.From(methodId));
            }

        }
    }

    internal Symbol PrepareName(Symbol symbol, ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> suffix)
    {
        var name = symbolTable.NameOf(symbol);
        var length = name.Length + prefix.Length + suffix.Length;
        var offset = 0;
        Span<byte> buffer = stackalloc byte[length];
        if (!prefix.IsEmpty)
        {
            prefix.CopyTo(buffer);
            offset += prefix.Length;
        }
        name.CopyTo(buffer[offset..]);
        offset += name.Length;
        if (!suffix.IsEmpty)
        {
            suffix.CopyTo(buffer[offset..]);
        }

        return Intern(buffer);
    }

    internal Symbol PrepareInstanceVariableName(Symbol symbol)
    {
        symbol = PrepareName(symbol, "@"u8, default);
        EnsureInstanceVariableName(symbol);
        return symbol;
    }

    void DefineSingletonMethod(RObject o, Symbol methodId, MRubyMethod method)
    {
        PrepareSingletonClass(o);
        DefineMethod(o.Class, methodId, method);
    }

    RClass CloneSingletonClass(MRubyValue obj)
    {
        var klass = obj.As<RObject>().Class;
        if (klass.VType != MRubyVType.SClass) return klass;

        // copy singleton(unnamed) class
        var clone = new RClass(ClassClass, MRubyVType.SClass)
        {
            Super = klass.Super,
            InstanceVType = klass.InstanceVType,
        };

        if (obj.VType is not (MRubyVType.Class or MRubyVType.SClass))
        {
            clone.Class = CloneSingletonClass(MRubyValue.From(klass));
        }

        // copy instance variables
        clone.InstanceVariables.Clear();
        klass.InstanceVariables.CopyTo(clone.InstanceVariables);
        clone.InstanceVariables.Set(Names.AttachedKey, obj);

        // copy method table
        clone.MethodTable.Clear();
        klass.MethodTable.CopyTo(clone.MethodTable);

        return clone;
    }
}

