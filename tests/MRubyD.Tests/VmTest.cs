using MRubyD.Compiler;

namespace MRubyD.Tests;

[TestFixture]
public class VmTest
{
    MRubyState mrb = default!;
    MRubyCompiler compiler = default!;

    [SetUp]
    public void Before()
    {
        mrb = MRubyState.Create();
        compiler = MRubyCompiler.Create(mrb);
    }

    [TearDown]
    public void After()
    {
        compiler.Dispose();
    }

    [Test]
    public void Recursive()
    {
        var result = Exec("""
                          def fibonacci(n)
                            return n if n <= 1 
                            fibonacci(n - 1) + fibonacci(n - 2)
                          end

                          fibonacci 10
                          """u8);
        Assert.That(result, Is.EqualTo(MRubyValue.From(55)));
    }

    [Test]
    public void StackOverflow()
    {
        Assert.Throws<MRubyRaiseException>(() =>
        {
            Exec("""
                 def f(x)
                   f(x + 1)
                 end
                 f(1)
                 """u8);
        });
    }

    [Test]
    public void Closure()
    {
         var result = Exec("""
                           def fb
                             n = 0
                             Proc.new do
                               n += 1
                               case
                               when n % 15 == 0
                               else n
                               end
                             end
                           end
                           fb.call
                           """u8);
         Assert.That(result, Is.EqualTo(MRubyValue.From(1)));
    }

    [Test]
    public void CatchHandler()
    {
        var result = Exec("""
                          loops = 0
                          limit = 2
                          loop do
                            begin
                              limit -= 1
                              break unless limit > 0
                              raise "!"
                            rescue
                              redo
                            ensure
                              loops += 1
                            end
                          end
                          loops
                          """u8);
        Assert.That(result, Is.EqualTo(MRubyValue.From(2)));
    }

    [Test]
    public void Include()
    {
        var result = Exec("""
                          module M
                            def foo
                              123
                            end
                          end
                          
                          class A
                            include M
                          end
                          
                          A.new.foo
                          """u8);
        Assert.That(result, Is.EqualTo(MRubyValue.From(123)));
    }

    [Test]
    public void ClassEval()
    {
        var result = Exec("""
                          c = Class.new do
                            def foo
                              123
                            end
                          end
                          c.new.foo
                          """u8);
        Assert.That(result, Is.EqualTo(MRubyValue.From(123)));
    }

    [Test]
    public void NewWithBlock()
    {
        var result = Exec("""
                          a = Array.new(1) { 123 }
                          a[0]
                          """u8);
        Assert.That(result, Is.EqualTo(MRubyValue.From(123)));
    }

    MRubyValue Exec(ReadOnlySpan<byte> code)
    {
        var irep = compiler.Compile(code);
        return mrb.Exec(irep);
    }
}
