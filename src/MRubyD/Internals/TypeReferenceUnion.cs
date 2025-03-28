using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace MRubyD.Internals;

[DebuggerDisplay("Value = {BoxedValue}")]
internal readonly struct TypeReferenceUnion : IEquatable<TypeReferenceUnion>
{
    public bool Equals(TypeReferenceUnion other)
    {
        return ReferenceEquals(RawReference,other.RawReference);
    }

    public override bool Equals(object? obj)
    {
        return obj is TypeReferenceUnion other && Equals(other);
    }

    public override int GetHashCode()
    {
        return RawReference.GetHashCode();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TypeReferenceUnion(RObject reference) => RawReference = reference;
    public TypeReferenceUnion(InternalMRubyType tag) => Unsafe.As<TypeReferenceUnion, nint>(ref this) = (nint)tag;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public readonly RObject RawReference;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    
    public nint RawTagValue
    {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Unsafe.As<TypeReferenceUnion, nint>(ref Unsafe.AsRef(in this));
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public InternalMRubyType TagValue
    {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (InternalMRubyType)RawTagValue;
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]

    public RObject? Object
    {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => RawTagValue > 255 ? RawReference : null;
    }

    public bool IsType
    {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => RawTagValue <= 255;
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public bool IsReference
    {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => RawTagValue > 255;
    }

    public object BoxedValue => IsType ? TagValue : RawReference;
    public override string ToString() => BoxedValue.ToString()!;
    
    public static bool operator ==(TypeReferenceUnion left, TypeReferenceUnion right) => ReferenceEquals(left.RawReference,right.RawReference);

    public static bool operator !=(TypeReferenceUnion left, TypeReferenceUnion right) => !ReferenceEquals(left.RawReference,right.RawReference);
}