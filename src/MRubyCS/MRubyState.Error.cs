using System;
using MRubyCS.Internals;
using Utf8StringInterpolation;

namespace MRubyCS;

public class MRubyLongJumpException(string message) : Exception(message);

public class MRubyBreakException(MRubyState state, RBreak breakObject)
    : MRubyLongJumpException("break")
{
    public MRubyState State => state;
    public RBreak BreakObject => breakObject;
}

public class MRubyRaiseException(
    string message,
    MRubyState state,
    RException exceptionObject,
    int callDepth)
    : MRubyLongJumpException(message)
{
    public MRubyState State { get; } = state;
    public RException ExceptionObject { get; } = exceptionObject;
    public int CallDepth { get; } = callDepth;

    public MRubyRaiseException(
        MRubyState state,
        RException exceptionObject,
        int callDepth)
        : this(exceptionObject.Message?.ToString() ?? "exception raised", state, exceptionObject, callDepth)
    {
    }
}

partial class MRubyState
{
    public void Raise(RException ex)
    {
        var typeName = NameOf(ex.Class);

        var message = ex.Message?.Length >  0
            ? $"{ex.Message.ToString()} ({typeName.ToString()})"
            : typeName.ToString();

        Exception = new MRubyRaiseException(message, this, ex, context.CallDepth);
        throw Exception;
    }

    public void Raise(RClass exceptionClass, RString message)
    {
        var backtrace = Backtrace.Capture(context);
        var ex = new RException(message, exceptionClass)
        {
            Backtrace = backtrace
        };
        Raise(ex);
    }

    public void RaiseConstMissing(RClass mod, Symbol name)
    {
        if (mod.GetRealClass() != ObjectClass)
        {
            RaiseNameError(name, NewString($"uninitialized constant {NameOf(mod)}::{NameOf(name)}"));
        }
        else
        {
            RaiseNameError(name, NewString($"uninitialized constant {NameOf(name)}"));
        }
    }

    public void RaiseArgumentError(int argc, int min, int max)
    {
        RString message;
        if (min == max)
        {
            message = NewString($"wrong number of arguments (given {argc}, expected {min})");
        }
        else if (max < 0)
        {
            message = NewString($"wrong number of arguments (given {argc}, expected {min}+)");
        }
        else
        {
            message = NewString($"wrong number of arguments (given {argc}, expected {min}..{max})");
        }

        Raise(Names.ArgumentError, message);
    }

    public void RaiseMethodMissing(Symbol methodId, MRubyValue self, MRubyValue args)
    {
        var exceptionClass = GetExceptionClass(Names.NoMethodError);
        var ex = new RException(NewString($"undefined method {NameOf(methodId)} for {ClassNameOf(self)}"), exceptionClass);
        ex.InstanceVariables.Set(Names.NameVariable, MRubyValue.From(methodId));
        ex.InstanceVariables.Set(Names.ArgsVariable, args);
        Raise(ex);
    }

    public void RaiseNoMethodError(Symbol methodId, RString message)
    {
    }

    public void RaiseNameError(Symbol name, RString message)
    {
        var ex = new RException(message, GetExceptionClass(Names.NameError));
        ex.InstanceVariables.Set(Names.NameVariable, MRubyValue.From(name));
        Raise(ex);
    }

    public void EnsureBlockGiven(MRubyValue block)
    {
        if (block.IsNil)
        {
            Raise(Names.ArgumentError, "no block given"u8);
        }
        if (!block.IsProc)
        {
            Raise(Names.TypeError, "not a block"u8);
        }
    }

    public void EnsureNotFrozen(MRubyValue value)
    {
        if (value.IsImmediate || value.Object.IsFrozen)
        {
            RaiseFrozenError(value);
        }
    }

    public void EnsureNotFrozen(RObject o)
    {
        if (o.IsFrozen)
        {
            RaiseFrozenError(MRubyValue.From(o));
        }
    }

    internal void EnsureConstName(Symbol constName)
    {
        if (!NamingRule.IsConstName(NameOf(constName)))
        {
            var ex = new RException(
                NewString($"wrong constant name {NameOf(constName)}"),
                GetExceptionClass(Names.NameError));

            ex.InstanceVariables.Set(Names.NameVariable, MRubyValue.From(constName));
            Raise(ex);
        }
    }

    internal void EnsureInstanceVariableName(Symbol instanceVariableName)
    {
        if (!NamingRule.IsInstanceVariableName(NameOf(instanceVariableName)))
        {
            var ex = new RException(
                NewString($"'{NameOf(instanceVariableName)}' is not allowed as an instance variable name."),
                GetExceptionClass(Names.NameError));

            ex.InstanceVariables.Set(Names.NameVariable, MRubyValue.From(instanceVariableName));
            Raise(ex);
        }
    }

    internal void EnsureFloatValue(double value)
    {
        if (double.IsNaN(value))
        {
            Raise(Names.FloatDomainError, "NaN"u8);
        }
        if (double.IsPositiveInfinity(value))
        {
            Raise(Names.FloatDomainError, "Infinity"u8);
        }
        if (double.IsNegativeInfinity(value))
        {
            Raise(Names.FloatDomainError, "-Infinity"u8);
        }
    }

    internal void RaiseArgumentNumberError(int num)
    {
        var argc = (int)context.CurrentCallInfo.ArgumentCount;
        if (argc == MRubyCallInfo.CallMaxArgs)
        {
            var args = context.CurrentStack[1];
            if (args.VType == MRubyVType.Array)
            {
                argc = args.As<RArray>().Length;
            }
        }

        if (argc == 0 &&
            context.CurrentCallInfo.KeywordArgumentCount != 0 &&
            context.CurrentStack[1].As<RHash>().Length > 0)
        {
            argc++;
        }

        var message = NewString($"wrong number of arguments (given {argc}, expected {num})");
        Raise(Names.ArgumentError, message);
    }

    internal void Raise(RClass exceptionClass, ReadOnlySpan<byte> message)
    {
        Raise(exceptionClass, NewString(message));
    }

    internal void Raise(Symbol errorType, ReadOnlySpan<byte> message)
    {
        Raise(GetExceptionClass(errorType), NewString(message));
    }

    internal void Raise(Symbol errorType, RString message)
    {
        Raise(GetExceptionClass(errorType), message);
    }

    internal void RaiseFrozenError(MRubyValue v)
    {
        Raise(Names.FrozenError, Utf8String.Format($"can't modify frozen {Stringify(v)}"));
    }

    internal void EnsureValueIsConst(MRubyValue value)
    {
        if (value.VType is not (MRubyVType.Class or MRubyVType.Module or MRubyVType.SClass))
        {
            Raise(Names.TypeError, "constant is non class/module"u8);
        }
    }

    internal void EnsureValueIsBlock(MRubyValue value)
    {
        if (!value.IsProc)
        {
            Raise(Names.TypeError, "not a block"u8);
        }
    }

    internal void EnsureClassOrModule(MRubyValue value)
    {
        if (!value.IsClass)
        {
            Raise(Names.TypeError, NewString($"{Stringify(value)} is not a class/module"));
        }
    }

    internal void EnsureInheritable(RClass c)
    {
        if (c.VType != MRubyVType.Class)
        {
            Raise(Names.TypeError, NewString($"superclass must be a Class ({NameOf(c)} given)"));
        }
        if (c.VType != MRubyVType.SClass)
        {
            Raise(Names.TypeError, "can't make subclass of singleton class"u8);
        }
        if (c == ClassClass)
        {
            Raise(Names.TypeError, "can't make subclass of Class"u8);
        }
    }

    internal void EnsureValueType(MRubyValue value, MRubyVType expectedType)
    {
        if (value.VType == expectedType) return;

        RString actualValueName;
        if (value.IsNil)
        {
            actualValueName = NewString("nil"u8);
        }
        else if (value.IsInteger)
        {
            actualValueName = NewString("Integer"u8);
        }
        else if (value.IsSymbol)
        {
            actualValueName = NewString("Symbol"u8);
        }
        else if (value.IsImmediate)
        {
            actualValueName = Stringify(value);
        }
        else
        {
            actualValueName = NameOf(ClassOf(value));
        }
        Raise(Names.TypeError, NewString($"wrong argument type {actualValueName} (expected {expectedType})"));
    }

    RClass GetExceptionClass(Symbol name)
    {
        if (!TryGetConst(name, out var value) || value.VType != MRubyVType.Class)
        {
            Raise(ExceptionClass, "exception corrupted"u8);
        }

        var exceptionClass = value.As<RClass>();
        if (!exceptionClass.Is(ExceptionClass))
        {
            Raise(ExceptionClass, "non-exception raised"u8);
        }
        return exceptionClass;
    }
}
