namespace MRubyD.Tests;

public class RHashTest
{
    [Test]
    public void MRubyValueKey()
    {
        var state = MRubyState.Create();
        var h = state.NewHash(2);

        var a1 = state.NewString("a"u8);
        var a2 = state.NewString("a"u8);

        h.Add(MRubyValue.From(a1), MRubyValue.True);
        Assert.That(h.TryGetValue(MRubyValue.From(a2), out _), Is.True);
    }
}
