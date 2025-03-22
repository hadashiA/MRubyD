namespace MRubyD.Tests;

[TestFixture]
public class TypeSystemTest
{
    MRubyState mrb = default!;

    [OneTimeSetUp]
    public void BeforeAll()
    {
        mrb = MRubyState.Create();
    }
}

