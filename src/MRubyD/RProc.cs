using MRubyD.Internals;

namespace MRubyD;

public interface ICallScope
{
    public RClass TargetClass { get; }
}

/// <summary>
/// Closure captured context
/// </summary>
class REnv() : RBasic(MRubyVType.Env, default!), ICallScope
{
    public Memory<MRubyValue> Stack { get; set; } = Memory<MRubyValue>.Empty;
    public required int BlockArgumentOffset { get; init; }
    public required MRubyContext? Context { get; init; }
    public required RClass TargetClass { get; init; }
    public required Symbol MethodId { get; init; }

    public bool OnStack => Context != null;

    public void CaptureStack()
    {
        if (Stack.Length == 0)
        {
            Stack = Memory<MRubyValue>.Empty;
        }

        Stack = new Memory<MRubyValue>(Stack.ToArray());
    }
}

public abstract class RProc(RClass procClass) : RObject(MRubyVType.Proc, procClass), IEquatable<RProc>
{
    public required RProc? Upper { get; init; }
    public required ICallScope? Scope
    {
        get => scope;
        init => scope = value;
    }

    ICallScope? scope;

    internal RProc FindReturningDestination(out REnv? env)
    {
        var p = this;
        env = p.Scope as REnv;
        while (p.Upper != null)
        {
            if (p.HasFlag(MRubyObjectFlags.ProcScope | MRubyObjectFlags.ProcStrict))
            {
                return p;
            }
            env = p.Scope as REnv;
            p = p.Upper;
        }
        return p;
    }

    internal REnv? FindUpperEnvTo(int up)
    {
        RProc? proc = this;
        while (up-- > 0)
        {
            proc = proc.Upper;
            if (proc is null) return null;
        }
        return proc.Scope as REnv;
    }


    internal void UpdateScope(ICallScope scope)
    {
        this.scope = scope;
    }

    public abstract RProc Dup();

    internal override RObject Clone()
    {
        var clone = Dup();
        InstanceVariables.CopyTo(clone.InstanceVariables);
        return clone;
    }

    public abstract bool Equals(RProc? other);

    public static bool operator ==(RProc? a, RProc? b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (ReferenceEquals(a, null) || ReferenceEquals(b, null))
            return false;
        return a.Equals(b);
    }

    public static bool operator !=(RProc? a, RProc? b) => !(a == b);

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((RProc)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Upper, Scope);
    }

}

public sealed class IrepProc : RProc, IEquatable<RProc>
{
    public Irep Irep { get; }
    public int ProgramCounter { get; }

    internal IrepProc(Irep irep, int pc, RClass procClass) : base(procClass)
    {
        Irep = irep;
        ProgramCounter = pc;
    }

    public override RProc Dup()
    {
        var clone = new IrepProc(Irep, ProgramCounter, Class)
        {
            Upper = Upper,
            Scope = Scope,
        };
        clone.SetFlag(Flags);
        return clone;
    }

    public override bool Equals(RProc? other)
    {
        if (other is IrepProc otherProc)
        {
            return Irep == otherProc.Irep && ProgramCounter == otherProc.ProgramCounter;
        }
        return false;
    }
}

public sealed class MethodAliasProc : RProc
{
    public Symbol MethodId { get; }

    internal MethodAliasProc(Symbol methodId, RClass procClass) : base(procClass)
    {
        MethodId = methodId;
    }

    public override RProc Dup()
    {
        var clone = new MethodAliasProc(MethodId, Class)
        {
            Upper = Upper,
            Scope = Scope,
        };
        clone.SetFlag(Flags);
        return clone;
    }

    public override bool Equals(RProc? other)
    {
        if (other is MethodAliasProc otherProc)
        {
            return MethodId == otherProc.MethodId;
        }
        return false;
    }
}
