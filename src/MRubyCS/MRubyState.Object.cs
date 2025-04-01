using System;
using System.Buffers;
using System.Buffers.Text;
using System.Runtime.CompilerServices;
using MRubyCS.Internals;
using MRubyCS.StdLib;
using Utf8StringInterpolation;

namespace MRubyCS;

partial class MRubyState
{
    public RString NewString(int capacity) => new(capacity, StringClass);

    public RString NewString(ReadOnlySpan<byte> utf8) => new(utf8, StringClass);

    public RString NewString(ref Utf8StringWriter<ArrayBufferWriter<byte>> format)
    {
        format.Flush();
        return NewString(format.GetBufferWriter().WrittenSpan);
    }

    public RString NewStringOwned(byte[] buffer) => RString.Owned(buffer, StringClass);

    public RString NewStringOwned(byte[] buffer, int length) => RString.Owned(buffer, length, StringClass);

    public RArray NewArray(int capacity) => new(capacity, ArrayClass);

    public RArray NewArray(params ReadOnlySpan<MRubyValue> values) => new(values, ArrayClass);

    public RHash NewHash(int capacity) => new(capacity, valueEqualityComparer, HashClass);

    public MRubyValue NewInteger(long x)
    {
        return MRubyValue.From(x);
    }

    public Symbol ToSymbol(MRubyValue value)
    {
        if (value.IsSymbol) return value.SymbolValue;
        if (value.VType == MRubyVType.String) return Intern(value.As<RString>());
        Raise(Names.TypeError, NewString($"{Stringify(value)} is not a symbol nor a string"));
        return default; // not reached
    }

    public long ToInteger(MRubyValue value)
    {
        if (value.IsInteger) return value.IntegerValue;

        if (value.IsFloat)
        {
            return FloatMembers.ToI.Invoke(this, value).IntegerValue;
        }

        // TODO: more numeric types

        Raise(Names.TypeError, NewString($"{Stringify(value)} cannot be converted to Integer"));
        return default;
    }

    public double ToFloat(MRubyValue value)
    {
        if (value.IsNil)
        {
            Raise(Names.TypeError, "can't convert nil into Float"u8);
        }

        switch (value.VType)
        {
            case MRubyVType.Integer:
                return value.IntegerValue;
            case MRubyVType.Float:
                return value.FloatValue;
        }
        Raise(Names.TypeError, NewString($"{Stringify(value)} cannot be converted to Float"));
        return default;
    }

    public RString NameOf(Symbol symbol)
    {
        var result = NewString(symbolTable.NameOf(symbol));
        result.MarkAsFrozen();
        return result;
    }

    public RString NameOf(RClass c)
    {
        if (c.InstanceVariables.TryGet(Names.ClassNameKey, out var className))
        {
            // no name (yet)
            if (className.IsSymbol)
            {
                var path = ClassPath.Find(this, c);
                if (path.Length <= 1)
                {
                    var name = NameOf(className.SymbolValue);
                    c.InstanceVariables.Set(Names.ClassNameKey, MRubyValue.From(name));
                    return name.Dup();
                }
                var pathName = path.ToRString(this);
                c.InstanceVariables.Set(Names.ClassNameKey, MRubyValue.From(pathName));
                return pathName.Dup();
            }
            // already cached
            if (className.VType == MRubyVType.String)
            {
                return className.As<RString>().Dup();
            }
        }
        // top level class or module
        var prefix = c.VType == MRubyVType.Module
            ? "Module"u8
            : "Class"u8;
        var h = RuntimeHelpers.GetHashCode(c);
        var instantName = NewString(Utf8String.Format($"#<{prefix}:{h}>"));
        c.InstanceVariables.Set(Names.ClassNameKey, MRubyValue.From(instantName));
        return instantName;
    }

    public RClass ClassOf(MRubyValue value)
    {
        if (value.Object is { } o) return o.Class;
        if (value.IsNil) return NilClass;
        if (value.IsFalse) return FalseClass;
        if (value.IsTrue) return TrueClass;
        if (value.IsSymbol) return SymbolClass;
        if (value.IsInteger) return IntegerClass;
        if (value.IsFloat) return FloatClass;
        Raise(Names.RuntimeError, "no class found"u8);
        return default!;
    }

    public RString ClassNameOf(MRubyValue value)
    {
        return NameOf(ClassOf(value));
    }

    public bool InstanceOf(MRubyValue value, RClass c)
    {
        return c == ClassOf(value).GetRealClass();
    }

    public bool KindOf(MRubyValue value, RClass c)
    {
        var classOfValue = ClassOf(value);
        c = c.AsOrigin();
        while (classOfValue != null!)
        {
            if (classOfValue.Class == c || classOfValue.MethodTable == c.MethodTable)
            {
                return true;
            }
            classOfValue = classOfValue.Super;
        }
        return false;
    }

    public bool ValueEquals(MRubyValue a, MRubyValue b)
    {
        if (a == b) return true;
        // value mixing with integer and float
        if (a.IsInteger && b.IsFloat)
        {
            if ((double)a.IntegerValue == b.FloatValue) return true;
        }
        else if (a.IsFloat && b.IsInteger)
        {
            if (a.FloatValue == (float)b.IntegerValue) return true;
        }
        else
        {
            if (TryFindMethod(ClassOf(a), Names.OpEq, out var method, out _) &&
                method != BasicObjectMembers.OpEq)
            {
                var result = Send(a, Names.OpEq, b);
                return result.Truthy;
            }
        }
        return false;
    }

    public int ValueCompare(MRubyValue a, MRubyValue b)
    {
        switch (a.VType)
        {
            case MRubyVType.Integer:
            case MRubyVType.Float:
                return NumberCompare(a, b);
            case MRubyVType.String:
                if (b.Object is RString bStr)
                {
                    return a.As<RString>().CompareTo(bStr);
                }
                return -2;
            default:
                var result = Send(a, Names.OpCmp, b);
                if (result.IsInteger) return (int)result.IntegerValue;
                return -2;
        }
    }

    public RString Stringify(MRubyValue value)
    {
        switch (value.VType)
        {
            case MRubyVType.String:
                return value.As<RString>();
            case MRubyVType.Symbol:
                return NameOf(value.SymbolValue);
            case MRubyVType.Integer:
                return StringifyInteger(value, 10);
            case MRubyVType.SClass:
            case MRubyVType.Class:
            case MRubyVType.Module:
                return StringifyModule(value.As<RClass>());
            default:
                return ConvertType(value, MRubyVType.String, Names.ToS)
                    .As<RString>();
        }
    }

    public RString StringifyAny(MRubyValue value)
    {
        var className = NameOf(ClassOf(value));
        var rawString = value.IsImmediate
            ? Utf8String.Format($"#<{className}>")
            : Utf8String.Format($"#<{className}:{value.Object!.GetHashCode()}>");
        return NewStringOwned(rawString);
    }

    public MRubyValue GetInstanceVariable(MRubyValue obj, Symbol key)
    {
        if (obj.Object is RClass c)
        {
            return c.ClassInstanceVariableTable.Get(key);
        }

        if (obj.Object is { } o)
        {
            return o.InstanceVariables.Get(key);
        }
        return MRubyValue.Nil;
    }

    public void SetInstanceVariable(MRubyValue obj, Symbol key, MRubyValue value)
    {
        EnsureNotFrozen(obj);
        if (obj.Object is RClass c)
        {
            c.ClassInstanceVariableTable.Set(key, value);
        }
        else
        {
            obj.As<RObject>().InstanceVariables.Set(key, value);
        }
    }

    public MRubyValue RemoveInstanceVariable(MRubyValue obj, Symbol key)
    {
        EnsureNotFrozen(obj);
        if (obj.As<RObject>().InstanceVariables.Remove(key, out var removedValue))
        {
            return removedValue;
        }
        return MRubyValue.Undef;
    }

    public MRubyValue CloneObject(MRubyValue obj)
    {
        if (obj.Object is RObject src)
        {
            if (src.VType == MRubyVType.SClass)
            {
                Raise(Names.TypeError, "can't clone singleton class"u8);
            }
            // Clone class (with singleton class)
            var clone = src.Clone();
            if (clone.VType is MRubyVType.Class or MRubyVType.Module)
            {
                clone.InstanceVariables.Remove(Names.ClassNameKey, out _);
            }
            clone.Class = CloneSingletonClass(obj);
            if (src.IsFrozen)
            {
                clone.MarkAsFrozen();
            }

            var cloneValue = MRubyValue.From(clone);
            if (TryFindMethod(clone.Class, Names.InitializeCopy, out var method, out _) &&
                method != KernelMembers.InitializeCopy)
            {
                Send(cloneValue, Intern("initialize_copy"u8), obj);
            }
            return cloneValue;
        }
        return obj;
    }

    public MRubyValue DupObject(MRubyValue obj)
    {
        if (obj.Object is RObject src)
        {
            if (src.VType == MRubyVType.SClass)
            {
                Raise(Names.TypeError, "can't clone singleton class"u8);
            }

            var clone = src.Clone();
            var cloneValue = MRubyValue.From(clone);

            if (TryFindMethod(clone.Class, Names.InitializeCopy, out var method, out _) &&
                method != KernelMembers.InitializeCopy)
            {
                Send(cloneValue, Intern("initialize_copy"u8), obj);
            }
            return cloneValue;
        }
        return obj;
    }

    public RString InspectObject(MRubyValue value)
    {
        if (value.Object is RObject obj)
        {
            if (obj.InstanceVariables.Length > 0)
            {
                var s = NewString($"-<{NameOf(obj.Class)}:{obj.GetHashCode()} ");
                if (context.IsRecursiveCalling(obj, Names.Inspect))
                {
                    s.Concat(" ...>"u8);
                    return s;
                }

                var first = true;
                foreach (var (k, v) in obj.InstanceVariables)
                {
                    var inspectedValue = Stringify(Send(v, Names.Inspect));
                    s.Concat(NewString($"{NameOf(k)}={inspectedValue} "));
                    if (!first)
                    {
                        s.Concat(", "u8);
                    }
                    first = false;
                }
                return s;
            }
        }
        return StringifyAny(value);
    }

    public MRubyValue SplatArray(MRubyValue value)
    {
        if (value.Object is RArray array)
        {
            return MRubyValue.From(array.Dup());
        }

        var methodId = Intern("to_a"u8);
        if (!RespondTo(value, methodId))
        {
            return MRubyValue.From(NewArray(value));
        }
        var convertedValue = Send(value, methodId);
        if (convertedValue.IsNil)
        {
            return MRubyValue.From(NewArray(value));
        }
        EnsureValueType(convertedValue, MRubyVType.Array);
        return MRubyValue.From(convertedValue.As<RArray>().Dup());
    }

    RProc NewProc(Irep irep, RClass? targetClass = null)
    {
        ref var callInfo = ref context.CurrentCallInfo;

        targetClass ??= (callInfo.Proc?.Scope ?? callInfo.Scope).TargetClass;
        return new RProc(irep, 0, ProcClass)
        {
            Upper = callInfo.Proc,
            Scope = targetClass
        };
    }

    RProc NewClosure(Irep irep)
    {
        ref var callInfo = ref context.CurrentCallInfo;
        var env = callInfo.Scope as REnv;

        if (env is null)
        {
            if (callInfo.Proc is { } upper)
            {
                var methodId = callInfo.MethodId;
                if (upper?.Scope is REnv { Context: null } x)
                {
                    methodId = x.MethodId;
                }

                var stackSize = upper!.Irep.RegisterVariableCount;
                env = new REnv
                {
                    Context = context,
                    Stack = context.Stack.AsMemory(callInfo.StackPointer, stackSize),
                    MethodId = methodId,
                    BlockArgumentOffset = callInfo.BlockArgumentOffset,
                    TargetClass = callInfo.Scope.TargetClass
                };
                callInfo.Scope = env;
            }
        }
        return new RProc(irep, 0, ProcClass)
        {
            Upper = callInfo.Proc,
            Scope = env,
        };
    }

    internal MRubyValue GetProcSelf(RProc proc, out RClass targetClass)
    {
        if (proc.Scope is REnv env)
        {
            if (env.Stack.Length <= 0)
            {
                Raise(Names.ArgumentError, "self is lost (probably ran out of memory when the block became independent)"u8);
            }
            targetClass = env.TargetClass;
            return env.Stack.Span[0];
        }

        targetClass = ObjectClass;
        return MRubyValue.From(TopSelf);
    }

    internal ReadOnlySpan<MRubyValue> GetProcEnvStack()
    {
        ref var callInfo = ref context.CurrentCallInfo;
        if (callInfo.Proc?.Scope is REnv env)
        {
            return env.Stack.Span;
        }
        Raise(Names.TypeError, "Can't get closure env from proc"u8);
        return default; // not reached
    }

    internal RString StringifyInteger(MRubyValue value, int bases)
    {
        if (bases is not (2 or 10 or 16))
        {
            Raise(Names.ArgumentError, Utf8String.Format($"invalid radix {bases}"));
        }

        var x = value.IntegerValue;
        var f = bases switch
        {
            2 => new StandardFormat('b'),
            16 => new StandardFormat('x'),
            _ => new StandardFormat('d')
        };

        Span<byte> buf = stackalloc byte[8];
        int bytesWritten;
        while (!Utf8Formatter.TryFormat(x, buf, out bytesWritten, f))
        {
            buf = stackalloc byte[buf.Length * 2];
        }
        return NewString(buf[..bytesWritten]);
    }

    MRubyValue ConvertType(MRubyValue value, MRubyVType vtype, Symbol convertMethodId)
    {
        if (!RespondTo(value, convertMethodId))
        {
            Raise(Names.TypeError, NewString($"can't convert type {value.VType} into {vtype}"));
            return MRubyValue.Nil;
        }
        return Send(value, convertMethodId);
    }

    RString StringifyModule(RClass c)
    {
        if (c.VType == MRubyVType.SClass)
        {
            var v = c.InstanceVariables.Get(Names.AttachedKey);
            var str = v.VType == MRubyVType.Class ? StringifyAny(v) : Stringify(v);
            return NewString($"#<Class:{str}>");
        }
        return NameOf(c);
    }

    int NumberCompare(MRubyValue a, MRubyValue b)
    {
        if (a.IsInteger && b.IsInteger)
        {
            return a.IntegerValue.CompareTo(b.IntegerValue);
        }

        if (b is { IsInteger: false, IsFloat: false })
        {
            var result = Send(b, Names.OpCmp, a);
            if (!result.IsInteger) return -2;
            return (int)-result.IntegerValue;
        }

        var x = ToFloat(a);
        var y = ToFloat(b);
        return x.CompareTo(y);
    }
}
