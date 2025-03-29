namespace MRubyCS.Tests;

public class RStringTest
{
    [Test]
    public void Equals()
    {
        var a1 = new RString("a"u8, null!);
        var a2 = new RString("a"u8, null!);
        Assert.That(a1 == a2, Is.True);
    }
}
