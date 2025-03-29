using System;
using System.Reflection;
using BenchmarkDotNet.Running;
using MRubyCS;
using MRubyCS.Benchmark;

BenchmarkSwitcher.FromAssembly(Assembly.GetEntryAssembly()!).Run(args);

// ---
// var loader = new RubyScriptLoader();
// var state = MRubyState.Create();
//
// using var bin = loader.CompileToBinaryFormat("bm_fib.rb");
//
// var result = state.Exec(bin.GetNativeData());
// Console.WriteLine(result);
//
// unsafe
// {
//     var stateNative = NativeMethods.MrbOpen();
//     var result2 = NativeMethods.MrbLoadIrep(stateNative, bin.DangerousGetHandle().ToPointer());
//     Console.WriteLine(result2.IntValue);
// }
