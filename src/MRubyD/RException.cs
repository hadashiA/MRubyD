namespace MRubyD;

public sealed class RException(
    RString? message,
    RClass exceptionClass)
    : RObject(MRubyVType.Exception, exceptionClass)
{
    public RString? Message { get; set; } = message;
    public Backtrace? Backtrace { get; set; }

    internal override RObject Clone()
    {
        var clone = new RException(Message, Class);
        InstanceVariables.CopyTo(clone.InstanceVariables);
        return clone;
    }
}

