using System;
using System.Runtime.CompilerServices;

namespace MRubyD;

public delegate MRubyValue MRubyFunc(MRubyState state, MRubyValue self);

[Flags]
public enum MRubyMethodKind
{
    RProc,
    CSharpFunc,
}

public readonly unsafe struct MRubyMethod : IEquatable<MRubyMethod>
{
    public static readonly MRubyMethod Nop = new((_, _) => MRubyValue.Nil);
    public static readonly MRubyMethod True = new((_, _) => MRubyValue.True);
    public static readonly MRubyMethod False = new((_, _) => MRubyValue.False);
    public static readonly MRubyMethod Identity = new((_, self) => self);

    public MRubyMethodKind Kind
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Proc != null ? MRubyMethodKind.RProc : MRubyMethodKind.CSharpFunc;
    }

    public readonly RProc? Proc;
    public readonly MRubyFunc? Func;
    // readonly delegate* managed<MRubyState, MRubyValue, MRubyValue> funcPtr = default;

    public MRubyMethod(RProc proc)
    {
        Proc = proc;
    }

    public MRubyMethod(MRubyFunc? func)
    {
        Func = func;
    }

    // internal MRubyMethod(delegate* managed<MRubyState, MRubyValue, MRubyValue> funcPtr)
    // {
    //     this.funcPtr = funcPtr;
    // }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue Invoke(MRubyState state, MRubyValue self)
    {
        return Func!.Invoke(state, self);
    }

    public bool Equals(MRubyMethod other)
    {
        return Proc == other.Proc && Func == other.Func;
    }

    public override bool Equals(object? obj)
    {
        return obj is MRubyMethod other && Equals(other);
    }

    public override int GetHashCode()
    {
        if (Proc != null)
        {
            return Proc.GetHashCode();
        }
        else
        {
            return Func!.GetHashCode();
        }
    }

    public static bool operator ==(MRubyMethod left, MRubyMethod right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(MRubyMethod left, MRubyMethod right)
    {
        return !(left == right);
    }
}
