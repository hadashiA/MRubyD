using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace MRubyD.Internals;

[DebuggerDisplay("Value = {BoxedValue}")]
internal readonly struct TypeOrReference : IEquatable<TypeOrReference>
{
    public bool Equals(TypeOrReference other)
    {
        return ReferenceEquals(RawReference,other.RawReference);
    }

    public override bool Equals(object? obj)
    {
        return obj is TypeOrReference other && Equals(other);
    }

    public override int GetHashCode()
    {
        return RawReference.GetHashCode();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TypeOrReference(RObject reference) => RawReference = reference;
    public TypeOrReference(InternalMRubyType tag) => Unsafe.As<TypeOrReference, nint>(ref this) = (nint)tag;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public readonly RObject RawReference;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    
    public nint RawTagValue
    {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Unsafe.As<TypeOrReference, nint>(ref Unsafe.AsRef(in this));
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
    
    public static bool operator ==(TypeOrReference left, TypeOrReference right) => ReferenceEquals(left.RawReference,right.RawReference);

    public static bool operator !=(TypeOrReference left, TypeOrReference right) => !ReferenceEquals(left.RawReference,right.RawReference);
}