using BenchmarkDotNet.Attributes;
using MRubyCS.Compiler;

namespace MRubyCS.Benchmark;

[Config(typeof(BenchmarkConfig))]
public class FibBenchmark
{
    readonly RubyScriptLoader scriptLoader = new();
    MrbNativeBytesHandle dataHandle = default!;

    [GlobalSetup]
    public void LoadScript()
    {
        dataHandle = scriptLoader.CompileToBinaryFormat("bm_fib.rb");
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        scriptLoader.Dispose();
    }

    [Benchmark]
    public void MRubyCS()
    {
        scriptLoader.RunMRubyCS(dataHandle.GetNativeData());
    }

    [Benchmark]
    public unsafe void MRubyNative()
    {
        scriptLoader.RunMRubyNative(dataHandle.GetNativeData());
    }
}
