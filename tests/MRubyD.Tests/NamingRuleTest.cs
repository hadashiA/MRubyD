using System.Text;
using MRubyD.Internals;

namespace MRubyD.Tests;

[TestFixture]
public class NamingRuleTest
{
    [Test]
    [TestCase("", "\"\"")]
    [TestCase("abcde123", "\"abcde123\"")]
    [TestCase("\"", "\"\\\"\"")]
    [TestCase("\n", "\"\\n\"")]
    public void Escape(string input, string expected)
    {
        var src = Encoding.UTF8.GetBytes(input);
        var dst = new byte[Encoding.UTF8.GetMaxByteCount(expected.Length)];
        var result = NamingRule.TryEscape(src, true, dst, out var written);

        Assert.That(result, Is.True);
        Assert.That(written, Is.EqualTo(Encoding.UTF8.GetBytes(expected).Length));
        Assert.That(Encoding.UTF8.GetString(dst[..written]), Is.EqualTo(expected));
    }
}
