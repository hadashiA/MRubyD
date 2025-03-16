namespace MRubyD;

public enum RangeCalculateResult
{
    /// <summary>
    /// (failure) not range
    /// </summary>
    TypeMismatch,

    /// <summary>
    /// (success) range
    /// </summary>
    Ok,

    /// <summary>
    /// (failure) out of range
    /// </summary>
    Out,
}

public sealed class RRange : RObject
{
    public MRubyValue Begin { get; }
    public MRubyValue End { get; }
    public bool Exclusive { get; }

    internal RRange(MRubyValue begin, MRubyValue end, bool exclusive, RClass rangeClass)
        : base(MRubyVType.Range, rangeClass)
    {
        Begin = begin;
        End = end;
        Exclusive = exclusive;
    }

    public RangeCalculateResult Calculate(
        int targetLength,
        bool truncate,
        out int calculatedOffset,
        out int calculatedLength)
    {
        var begin = Begin.IsNil ? 0 : (int)Begin.IntegerValue;
        var end = End.IsNil ? -1 : (int)End.IntegerValue;

        if (begin + targetLength < 0)
        {
            calculatedOffset = default;
            calculatedLength = default;
            return RangeCalculateResult.Out;
        }

        if (truncate)
        {
            if (begin > targetLength)
            {
                calculatedOffset = default;
                calculatedLength = default;
                return RangeCalculateResult.Out;
            }

            if (end > targetLength)
            {
                end = targetLength;
            }
        }

        if (end < 0) end += targetLength;
        if (!Exclusive && (!truncate || end > targetLength)) end++; // include end point

        calculatedLength = end - begin;
        if (calculatedLength < 0) calculatedLength = 0;

        calculatedOffset = begin;
        return RangeCalculateResult.Ok;
    }
}
