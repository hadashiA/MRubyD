using System;
using System.Runtime.InteropServices;

namespace MRubyD.Compiler;

public class MrbNativeBytesHandle : SafeHandle
{
    readonly MrbStateHandle stateHandle1;
    readonly int length1;

    internal MrbNativeBytesHandle(
        MrbStateHandle stateHandle,
        IntPtr ptr,
        int length) : base(ptr, true)
    {
        stateHandle1 = stateHandle;
        length1 = length;
    }

    public override bool IsInvalid => handle == IntPtr.Zero;
    public int Length => length1;

    public unsafe ReadOnlySpan<byte> GetNativeData()
    {
        return new ReadOnlySpan<byte>(DangerousGetHandle().ToPointer(), Length);
    }

    protected override unsafe bool ReleaseHandle()
    {
        if (IsClosed) return false;
        NativeMethods.MrbFree(stateHandle1.DangerousGetPtr(), DangerousGetHandle().ToPointer());
        return true;
    }
}
