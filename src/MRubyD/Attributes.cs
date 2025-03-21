using System;

namespace MRubyD;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
public class MRubyMethodAttribute : Attribute
{
    public int RequiredArguments = 0;
    public int OptionalArguments = 0;
    public bool RestArguments = false;
    public bool BlockArgument = false;
}