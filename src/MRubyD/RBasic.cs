using System;
using MRubyD.Internals;

namespace MRubyD;

[Flags]
public enum MRubyObjectFlags : byte
{
    None = 0,
    Frozen = 1,

    ClassInitialized = 1 << 1,
    ClassInherited = 1 << 2,
    ClassOrigin = 1 << 3,
    ClassPrepended = 1 << 4,

    ProcScope = 1 << 1,
    ProcStrict = 1 << 2,
    ProcOrphan = 1 << 4,
}

public class RBasic
{
    internal readonly InternalMRubyType InternalType;
    public MRubyVType VType => (MRubyVType)(InternalType - 1);
    public RClass Class { get; internal set; }

    public MRubyObjectFlags Flags { get; private set; }
    public bool IsFrozen => (Flags & MRubyObjectFlags.Frozen) > 0;
    public bool HasFlag(MRubyObjectFlags flag) => (Flags & flag) > 0;

    internal RBasic(InternalMRubyType vType, RClass c)
    {
        InternalType = vType;
        Class = c;
    }

    public void SetFlag(MRubyObjectFlags flag)
    {
        Flags |= flag;
    }

    public void ClearFlag(MRubyObjectFlags flag)
    {
        Flags &= ~flag;
    }

    public void MarkAsInitialized()
    {
        SetFlag(MRubyObjectFlags.ClassInitialized);
    }

    public void MarkAsFrozen()
    {
        SetFlag(MRubyObjectFlags.Frozen);
    }

    public void UnFreeze()
    {
        ClearFlag(MRubyObjectFlags.Frozen);
    }

    // for inspection
    // public unsafe IntPtr ToIntPtr()
    // {
    //     var x = this;
    //     ref var r = ref Unsafe.AsPointer(ref );
    // }
}

