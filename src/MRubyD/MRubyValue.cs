using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using MRubyD.Internals;
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
    public static  MRubyValue Nil => default;
    public static  MRubyValue False => new(InternalMRubyType.False,0);
    public static  MRubyValue True => new(InternalMRubyType.True,0);
    public static  MRubyValue Undef => new(InternalMRubyType.Undef,0);

    public static bool Fixable(long value) => value >= FixnumMin &&
                                              value <= FixnumMax;

    const int SymbolShift = 32;

    static readonly nint FixnumMin = nint.MinValue >> 1;
    static readonly nint FixnumMax = nint.MaxValue >> 1;
    public static MRubyValue From(bool value) =>new(value?InternalMRubyType.True:InternalMRubyType.False,0);
    
    public static MRubyValue From(RObject obj) => new(obj);
    public static MRubyValue From(RString obj) => new(obj,InternalMRubyType.String);
    public static MRubyValue From(RArray obj) => new(obj,InternalMRubyType.Array);
    public static MRubyValue From(RHash obj) => new(obj,InternalMRubyType.Hash);
    public static MRubyValue From(RRange obj) => new(obj,InternalMRubyType.Range);
    public static MRubyValue From(RClass obj) => new(obj);
    public static MRubyValue From(RProc obj) => new(obj,InternalMRubyType.Proc);
    public static MRubyValue From(RBreak obj) => new(obj,InternalMRubyType.Break);
    public static MRubyValue From(long value) => new(InternalMRubyType.Integer, value);
    public static MRubyValue From(Symbol symbol) => new(InternalMRubyType.Symbol, symbol.Value);

    public static MRubyValue From(double value)
    {
        // Assume that MRB_USE_FLOAT32 is not defined
        // Assume that MRB_WORDBOX_NO_FLOAT_TRUNCATE is not defined
        var bits = Unsafe.As<double, long>(ref value);
        return new MRubyValue(InternalMRubyType.Float, bits);
    }

    public RObject? Object
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => union.Object;
    }

    public MRubyVType VType => union.IsType?(MRubyVType)(union.RawTagValue-1): Object!.VType;
    
    internal InternalMRubyType InternalVType => union.IsType?(union.TagValue): Object!.InternalType;
    internal InternalMRubyType ImmediateInternalVType => union.TagValue;

    public bool IsNil => union.RawTagValue == 0;
    public bool IsFalse =>union.TagValue==InternalMRubyType.False;
    public bool IsTrue => union.TagValue==InternalMRubyType.True;
    public bool IsUndef => union.TagValue==InternalMRubyType.Undef;
    public bool IsSymbol =>union.TagValue==InternalMRubyType.Symbol;
    //public bool IsFixnum => (bits & 1) == 1;
    public bool IsObject => union.IsReference;
    public bool IsImmediate => union.IsType;

    public T As<T>() where T : RObject => (T)Object!;
    internal T UnsafeAs<T>() where T : RObject => Unsafe.As<T>(union.RawReference);

    internal RHash? AsHashOrNull()
    {
        if (bits == (long)InternalMRubyType.Hash)
        {
            return Unsafe.As<RHash>(union.RawReference);
        }
        return null;
    }
    
    public bool Truthy => 1 < union.RawTagValue;
    public bool Falsy => (nuint)union.RawTagValue<=1;

    public bool IsInteger => union.TagValue==InternalMRubyType.Integer;

    public bool IsFloat => union.TagValue==InternalMRubyType.Float;
    internal bool IsNumeric => union.TagValue is InternalMRubyType.Integer or InternalMRubyType.Float;
    public bool IsBreak => Object?.VType == MRubyVType.Break;
    public bool IsProc => Object?.VType == MRubyVType.Proc;
    public bool IsClass => VType is MRubyVType.Class or MRubyVType.SClass or MRubyVType.Module;
    public bool IsNamespace => VType is MRubyVType.Class or MRubyVType.Module;

    public bool BoolValue => union.TagValue is InternalMRubyType.True or InternalMRubyType.False;
    //public long FixnumValue => bits >> 1;
    public Symbol SymbolValue => new((uint)bits);

    public long IntegerValue => bits;

    public double FloatValue =>
        // Assume that MRB_USE_FLOAT32 is not defined
        // Assume that MRB_WORDBOX_NO_FLOAT_TRUNCATE is not defined
        Unsafe.As<long, double>(ref Unsafe.AsRef(in bits));
    
    internal double NumericValue { 
        get
    {
        if (union.TagValue == InternalMRubyType.Integer) return bits;
       return Unsafe.As<long, double>(ref Unsafe.AsRef(in bits));
    }}

    public long ObjectId
    {
        get
        {
            if (union.IsReference) return union.RawReference.GetHashCode();
            switch (union.TagValue)
            {
                 case InternalMRubyType.Free:
                 case InternalMRubyType.Undef :return 0;
                 case InternalMRubyType.Symbol: 
                 case InternalMRubyType.Integer :return  bits; 
                 case InternalMRubyType.Float :return  FloatValue.GetHashCode();
            }
            return (long)union.TagValue;
        }
    }

    readonly TypeOrReference union;
    internal readonly long bits;

    MRubyValue(RObject obj)
    {
       union = new (obj);
       bits = (long)obj.InternalType;
    }
    
    MRubyValue(RObject obj,InternalMRubyType type)
    {
       union = new (obj);
       bits = (long)type;
    }
    
    MRubyValue(InternalMRubyType type ,long bits)
    {
       union = new (type);
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
