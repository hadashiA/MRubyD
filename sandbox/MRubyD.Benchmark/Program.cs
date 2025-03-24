// See https://aka.ms/new-console-template for more information

using System.Reflection;
using BenchmarkDotNet.Running;
using MRubyD.Benchmark;


BenchmarkSwitcher.FromAssembly(Assembly.GetEntryAssembly()!).Run(args);

unsafe
{
    var mrb = NativeMethods.MrbOpen();
    var source = """
                 def fib n
                   return n if n < 2
                   fib(n-2) + fib(n-1)
                 end
                 fib(37)
                 """u8;
    nint sourceLength = source.Length;
    fixed (byte* sourcePtr = source)
    {
        var result = NativeMethods.MrbLoadNString(mrb, sourcePtr, sourceLength);
    }
}
