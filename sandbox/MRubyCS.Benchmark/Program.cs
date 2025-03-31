using System;
using System.Reflection;
using BenchmarkDotNet.Running;
using MRubyCS.Benchmark;

BenchmarkSwitcher.FromAssembly(Assembly.GetEntryAssembly()!).Run(args);

// ---
// using var loader = new RubyScriptLoader();
//
// loader.PreloadScriptFromFile("bm_fib.rb");
//
// var result = loader.RunMRubyCS();
// Console.WriteLine(result);
//
// var result2 = loader.RunMRubyNative();
// Console.WriteLine(result2.IntValue);