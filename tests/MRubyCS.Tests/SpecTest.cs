using System.Text.RegularExpressions;
using MRubyCS.Compiler;

namespace MRubyCS.Tests;

[TestFixture]
public class SpecTest
{
    MRubyState mrb = default!;
    MRubyCompiler compiler = default!;
    string rubyDir = default!;
    string rubyTestDir = default!;

    [OneTimeSetUp]
    public void BeforeAll()
    {
        rubyDir = Path.Join(TestContext.CurrentContext.TestDirectory, "ruby");
        rubyTestDir = Path.Join(rubyDir, "test");
    }

    [SetUp]
    public void Before()
    {
        mrb = MRubyState.Create();
        compiler = MRubyCompiler.Create(mrb);

        mrb.DefineMethod(mrb.ObjectClass, mrb.Intern("__report_result"u8), (state, _) =>
        {
            var title = state.GetArgAsString(0).ToString();
            var iso = state.GetArgAsString(1).ToString();
            var results = state.GetArg(2).As<RArray>().AsSpan();

            var index = 0;
            foreach (var result in results)
            {
                var rec = result.As<RArray>();
                var passed = rec[0].Truthy;
                var message = state.Stringify(rec[1]).ToString();

                if (!rec[2].IsNil)
                {
                    message += $"\n{state.Stringify(rec[2]).ToString()}";
                }

                var tag = passed ? "Passed" : "Failed";
                var prefix = string.IsNullOrEmpty(iso) ? "" : $"({iso}) ";
                TestContext.Out.WriteLine($"{prefix}{title} [{index}] {tag} {message}");
                Assert.That(passed, Is.True, $"{prefix}{title} [{index}] {message}");
                index++;
            }
            return MRubyValue.Nil;
        });

        // same as `File.fnmatch?`
        mrb.DefineMethod(mrb.ObjectClass, mrb.Intern("_str_match?"u8), (state, _) =>
        {
            var pattern = state.GetArgAsString(0).ToString();
            var str = state.GetArgAsString(1).ToString();

            var regexStr = $"^{Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".")}$";
            var regex = new Regex(regexStr, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return MRubyValue.From(regex.Match(str).Success);
        });

        compiler.LoadExecFile(Path.Join(rubyDir, "assert.rb"));
    }

    [TearDown]
    public void After()
    {
        compiler.Dispose();
    }

    [Test]
    [TestCase("bs_literal.rb")]
    [TestCase("bs_block.rb")]
    [TestCase("basicobject.rb")]
    [TestCase("object.rb")]
    [TestCase("nil.rb")]
    [TestCase("false.rb")]
    [TestCase("true.rb")]
    [TestCase("symbol.rb")]
    [TestCase("ensure.rb")]
    [TestCase("iterations.rb")]
    [TestCase("literals.rb")]
    [TestCase("unicode.rb")]
    [TestCase("syntax.rb")]
    [TestCase("lang.rb")]
    // typesystem
    [TestCase("superclass.rb")]
    [TestCase("class.rb")]
    [TestCase("module.rb")]
    [TestCase("methods.rb")]
    // lib
    // [TestCase("integer.rb")]
    // [TestCase("string.rb")]
    // [TestCase("array.rb")]
    // error
    [TestCase("exception.rb")]
    [TestCase("indexerror.rb")]
    [TestCase("typeerror.rb")]
    [TestCase("localjumperror.rb")]
    // [TestCase("namerror.rb")]
    public void RubyScript(string fileName)
    {
        Assert.Multiple(() =>
        {
            Exec(fileName);
        });
    }

    void Exec(string fileName)
    {
        compiler.LoadExecFile(Path.Join(rubyTestDir, fileName));
    }
}
