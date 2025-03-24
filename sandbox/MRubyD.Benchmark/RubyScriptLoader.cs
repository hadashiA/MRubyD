using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace MRubyD.Benchmark;

unsafe class RubyScriptLoader : IDisposable
{
    public MRubyState MRubyDState { get; } = MRubyState.Create();
    public MrbStateNative* MrbStateNative { get; } = NativeMethods.MrbOpen();

    bool disposed;

    public byte[] LoadBytecode(string fileName)
    {
        var path = GetAbsolutePath(Path.Join("ruby", fileName));
        return File.ReadAllBytes(path);
    }

    public void RunMRubyD(byte[] bin)
    {
        MRubyDState.Exec(bin);
    }

    public void RunMRubyNative(byte[] bin)
    {
        fixed (byte* ptr = bin)
        {
            NativeMethods.MrbLoadIrep(MrbStateNative, ptr);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposed)
        {
            return;
        }

        NativeMethods.MrbClose(MrbStateNative);
        disposed = true;
    }

    static string GetAbsolutePath(string relativePath, [CallerFilePath] string callerFilePath = "")
    {
        return Path.Join(Path.GetDirectoryName(callerFilePath)!, relativePath);
        // return Path.Join(Assembly.GetEntryAssembly()!.Location, relativePath);
    }
}