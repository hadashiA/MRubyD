using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using MRubyD.Internals;
using Utf8StringInterpolation;

namespace MRubyD;

public enum MRubyVType
{
    Nil=0,
    False,
    True,
    Symbol,
    Undef,
    Free,
    Float,
    Integer,
    CPtr,
    Object,
    Class,
    Module,
    IClass, // Include class
    SClass, // Singleton class
    Proc,
    Array,
    Hash,
    String,
    Range,
    Exception,
    Env,
    CData,
    Fiber,
    Struct,
    Istruct,
    Break,
    Complex,
    Rational,
    BigInt,
}

public static class MRubyVTypeExtensions
{
    public static ReadOnlySpan<byte> ToUtf8String(this MRubyVType vType)
    {
        return Utf8String.Format($"{vType}");
    }

    public static bool IsClass(this MRubyVType vType) => vType is MRubyVType.Class or MRubyVType.SClass or MRubyVType.Module;
}

public readonly struct MRubyValue : IEquatable<MRubyValue>
{
    public static MRubyValue Nil => default;
    public static MRubyValue False => new(MRubyVType.False, 0);
    public static MRubyValue True => new(MRubyVType.True, 0);
    public static MRubyValue Undef => new(MRubyVType.Undef, 0);

    public static MRubyValue From(bool value) => new(value ? MRubyVType.True : MRubyVType.False, 0);

    public static MRubyValue From(RObject obj) => new(obj);
    internal static MRubyValue From(RString obj) => new(obj, MRubyVType.String);
    internal static MRubyValue From(RArray obj) => new(obj, MRubyVType.Array);
    internal static MRubyValue From(RHash obj) => new(obj, MRubyVType.Hash);
    internal static MRubyValue From(RRange obj) => new(obj, MRubyVType.Range);
    internal static MRubyValue From(RClass obj) => new(obj);
    internal static MRubyValue From(RProc obj) => new(obj, MRubyVType.Proc);
    internal static MRubyValue From(RBreak obj) => new(obj, MRubyVType.Break);
    internal static MRubyValue From(long value) => new(MRubyVType.Integer, value);
    internal static MRubyValue From(Symbol symbol) => new(MRubyVType.Symbol, symbol.Value);

    public static MRubyValue From(double value)
    {
        // Assume that MRB_USE_FLOAT32 is not defined
        // Assume that MRB_WORDBOX_NO_FLOAT_TRUNCATE is not defined
        return new (MRubyVType.Float, Unsafe.BitCast<double,long>(value));
    }

    public RObject? Object
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => union.Object;
    }

    public MRubyVType VType => union.IsType ? union.TypeValue : Object!.VType;
    internal MRubyVType ImmediateVType => union.TypeValue;
    internal nint RawUnionValue => union.RawValue;
    public bool IsNil => union.RawValue == 0;
    public bool IsFalse => union.RawValue ==(long) MRubyVType.False;
    public bool IsTrue => union.RawValue == (long)MRubyVType.True;
    public bool IsUndef => union.RawValue == (long)MRubyVType.Undef;
    public bool IsSymbol => union.RawValue == (long)MRubyVType.Symbol;
    public bool IsObject => union.IsObject;
    public bool IsImmediate => union.IsType;

    public T As<T>() where T : RObject => (T)Object!;
    internal T UnsafeAs<T>() where T : RObject => Unsafe.As<T>(union.RawObject);

    public bool Truthy => 1 < union.RawValue;
    public bool Falsy => (nuint)union.RawValue <= 1;

    public bool IsInteger => union.RawValue == (long)MRubyVType.Integer;
    public bool IsFloat => union.RawValue ==(long) MRubyVType.Float;
    internal bool IsNumeric => (long)union.RawValue is (long)MRubyVType.Integer or (long)MRubyVType.Float;
    public bool IsBreak => union.IsObject && bits == (long)MRubyVType.Break;
    public bool IsProc => union.IsObject && bits == (long)MRubyVType.Proc;
    public bool IsClass => union.IsObject && bits is (long)MRubyVType.Class or (long)MRubyVType.SClass or  (long)MRubyVType.Module;
    public bool IsNamespace => union.IsObject && bits is (long)MRubyVType.Class or (long)MRubyVType.Module;

    public bool BoolValue => (long)union.RawValue is (long)MRubyVType.True or (long)MRubyVType.False;

    public Symbol SymbolValue => new((uint)bits);

    public long IntegerValue => bits;

    public double FloatValue =>
        // Assume that MRB_USE_FLOAT32 is not defined
        // Assume that MRB_WORDBOX_NO_FLOAT_TRUNCATE is not defined
        Unsafe.As<long, double>(ref Unsafe.AsRef(in bits));

    public long ObjectId
    {
        get
        {
            if (union.IsObject) return union.RawObject.GetHashCode();
            switch (union.TypeValue)
            {
                case MRubyVType.Free:
                case MRubyVType.Undef: return 0;
                case MRubyVType.Symbol:
                case MRubyVType.Integer: return bits;
                case MRubyVType.Float: return FloatValue.GetHashCode();
            }
            return union.RawValue;
        }
    }

    readonly TypeObjectUnion union;
    internal readonly long bits;

    MRubyValue(RObject obj)
    {
        union = new(obj);
        bits = (long)obj.VType;
    }

    MRubyValue(RObject obj, MRubyVType type)
    {
        union = new(obj);
        bits = (long)type;
    }

    MRubyValue(MRubyVType type, long bits)
    {
        union = new(type);
        this.bits = bits;
    }

    public bool Equals(MRubyValue other) => bits == other.bits &&
                                            union == other.union;

    public static bool operator ==(MRubyValue a, MRubyValue b) => a.Equals(b);
    public static bool operator !=(MRubyValue a, MRubyValue b) => !a.Equals(b);

    public override bool Equals(object? obj)
    {
        return obj is MRubyValue other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Object == null
            ? bits.GetHashCode()
            : Object?.GetHashCode() ?? 0;
    }

    public override string ToString()
    {
        if (Object is { } x) return x.ToString()!;
        if (IsNil) return "nil";

        switch (VType)
        {
            case MRubyVType.False:
                return "false";
            case MRubyVType.True:
                return "true";
            case MRubyVType.Undef:
                return "undef";
            case MRubyVType.Symbol:
                return SymbolValue.ToString();
            case MRubyVType.Float:
                return FloatValue.ToString(CultureInfo.InvariantCulture);
            case MRubyVType.Integer:
                return IntegerValue.ToString(CultureInfo.InvariantCulture);
            default:
                return VType.ToString();
        }
    }
}