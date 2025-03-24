using BenchmarkDotNet.Attributes;

namespace MRubyD.Benchmark;

[Config(typeof(BenchmarkConfig))]
public class FibBenchmark
{
    readonly RubyScriptLoader scriptLoader = new();
    byte[] bin = default!;

    [GlobalSetup]
    public void LoadScript()
    {
         bin = scriptLoader.LoadBytecode("bm_fib.mrb");
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        scriptLoader.Dispose();
    }

    [Benchmark]
    public void MRubyD()
    {
        scriptLoader.RunMRubyD(bin);
    }

    [Benchmark]
    public unsafe void MRubyNative()
    {
        scriptLoader.RunMRubyNative(bin);
    }
}
