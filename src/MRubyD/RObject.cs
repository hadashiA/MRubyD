using MRubyD.Internals;

namespace MRubyD;

public class RObject : RBasic
{
    internal VariableTable InstanceVariables { get; } = new();

    internal RObject(InternalMRubyType vType, RClass klass) : base(vType, klass)
    {
    }

    /// <summary>
    /// Create a copy of the object (equivalent to `init_copy`)
    /// </summary>
    /// <remarks>
    ///
    /// Because of the ruby specification, overrideable processes are implemented with `initialize_copy`.
    /// </remarks>
    internal virtual RObject Clone()
    {
        var clone = new RObject(InternalType, Class);
        InstanceVariables.CopyTo(clone.InstanceVariables);
        return clone;
    }
}

