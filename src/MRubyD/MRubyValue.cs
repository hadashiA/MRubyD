using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using Utf8StringInterpolation;

namespace MRubyD;

public enum MRubyVType
{
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

// mrb_value representation:
//
// 64-bit word with inline float:
//   nil   : ...0000 0000 (all bits are 0)
//   false : ...0000 0100 (mrb_fixnum(v) != 0)
//   true  : ...0000 1100
//   undef : ...0001 0100
//   symbol: ...0001 1100 (use only upper 32-bit as symbol value with MRB_64BIT)
//   fixnum: ...IIII III1
//   float : ...FFFF FF10 (51 bit significands; require MRB_64BIT)
//   object: ...PPPP P000
public readonly struct MRubyValue : IEquatable<MRubyValue>
{
    public static readonly MRubyValue Nil = new(0);
    public static readonly MRubyValue False = new(0b0100);
    public static readonly MRubyValue True = new(0b1100);
    public static readonly MRubyValue Undef = new(0b0001_0100);

    public static bool Fixable(long value) => value >= FixnumMin &&
                                              value <= FixnumMax;

    const int SymbolShift = 32;

    static readonly nint FixnumMin = nint.MinValue >> 1;
    static readonly nint FixnumMax = nint.MaxValue >> 1;

    public static MRubyValue From(bool value) => value ? True : False;
    public static MRubyValue From(RObject obj) => new(obj);
    public static MRubyValue From(int value) => new((value << 1) | 1);
    public static MRubyValue From(long value) => new((value << 1) | 1);
    public static MRubyValue From(Symbol symbol) => new(((long)symbol.Value << SymbolShift) | 0b1_0100);

    public static MRubyValue From(double value)
    {
        // Assume that MRB_USE_FLOAT32 is not defined
        // Assume that MRB_WORDBOX_NO_FLOAT_TRUNCATE is not defined
        var bits = Unsafe.As<double, long>(ref value);
        return new MRubyValue((bits & ~3) | 2);
    }

    public RObject? Object
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    }

    public MRubyVType VType => this switch
    {
        { IsObject: true } => Object!.VType,
        { IsTrue: true } => MRubyVType.True,
        { IsUndef: true } => MRubyVType.Undef,
        { IsSymbol: true } => MRubyVType.Symbol,
        { IsFixnum: true } => MRubyVType.Integer,
        { IsFloat: true } => MRubyVType.Float,
        _ => default
    };

    public bool IsNil => bits == 0 && Object == null;
    public bool IsFalse => bits == 0b0100;
    public bool IsTrue => bits == 0b1100;
    public bool IsUndef => bits == 0b0001_0100;
    public bool IsSymbol => (bits & 0b1_1111) == 0b1_0100;
    public bool IsFixnum => (bits & 1) == 1;
    public bool IsObject => Object != null;
    public bool IsImmediate => Object == null;

    public T As<T>() where T : RObject => (T)Object!;
    public bool Truthy => !IsFalse && !IsNil;
    public bool Falsy => IsNil || IsFalse;

    public bool IsInteger => IsFixnum ||
                             Object?.VType == MRubyVType.Integer;

    public bool IsFloat => (bits & 0b11) == 0b10;
    public bool IsBreak => Object?.VType == MRubyVType.Break;
    public bool IsProc => Object?.VType == MRubyVType.Proc;
    public bool IsClass => VType is MRubyVType.Class or MRubyVType.SClass or MRubyVType.Module;
    public bool IsNamespace => VType is MRubyVType.Class or MRubyVType.Module;

    public bool BoolValue => (bits & ~False.bits) != 0;
    public long FixnumValue => bits >> 1;
    public Symbol SymbolValue => new((uint)(bits >> SymbolShift));

    public long IntegerValue
    {
        get
        {
            if (Object?.VType == MRubyVType.Integer)
            {
                return As<RInteger>().Value;
            }
            return bits >> 1;
        }
    }

    public double FloatValue
    {
        get
        {
            // Assume that MRB_USE_FLOAT32 is not defined
            // Assume that MRB_WORDBOX_NO_FLOAT_TRUNCATE is not defined
            var fbits = bits & ~3;
            return Unsafe.As<long, double>(ref fbits);
        }
    }

    public long ObjectId
    {
        get
        {
            if (!IsImmediate)
            {
                if (IsInteger) return IntegerValue;
                if (IsFloat) return (long)FloatValue;
            }
            return bits;
        }
    }

    readonly long bits;

    MRubyValue(RObject obj)
    {
        Object = obj;
    }

    MRubyValue(long bits)
    {
        this.bits = bits;
    }

    public bool Equals(MRubyValue other) => bits == other.bits &&
                                            Object == other.Object;
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
