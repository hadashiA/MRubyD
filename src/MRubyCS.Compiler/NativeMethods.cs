using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MRubyCS.Compiler;

[StructLayout(LayoutKind.Sequential)]
struct MrbStateNative;

unsafe class NativeMethods
{
    const string DllName = "libmruby";

    public const int Ok = 0;
    public const int Failed = 11;

    static NativeMethods()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, DllImportResolver);
    }

    static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName == DllName)
        {
            var path = "runtimes";
            string extname;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                path = Path.Join(path, "win");
                extname = ".dll";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                path = Path.Join(path, "osx");
                extname = ".dylib";
            }
            else
            {
                path = Path.Join(path, "linux");
                extname = ".so";
            }

            switch (RuntimeInformation.ProcessArchitecture)
            {
                case Architecture.X86:
                    path += "-x86";
                    break;
                case Architecture.X64:
                    path += "-x64";
                    break;
                case Architecture.Arm64:
                    path += "-arm64";
                    break;
            }

            path = Path.Join(path, "native", $"{DllName}{extname}");
            return NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, path), assembly, searchPath);
        }
        return IntPtr.Zero;
    }

    [DllImport(DllName, EntryPoint = "mrb_open", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern MrbStateNative* MrbOpen();

    [DllImport(DllName, EntryPoint = "mrb_close", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void MrbClose(MrbStateNative* mrb);

    [DllImport(DllName, EntryPoint = "mrb_free", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void MrbFree(MrbStateNative* mrb, void *ptr);

    [DllImport(DllName, EntryPoint = "mrbcs_compile", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int MrbcsCompile(
        MrbStateNative* mrb,
        byte* source,
        int sourceLength,
        byte** bin,
        int* binLength,
        byte** errorMessage);

    [DllImport(DllName, EntryPoint = "mrbcs_compile_to_proc", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int MrbcsCompileToProc(
        MrbStateNative* mrb,
        byte* source,
        int sourceLength,
        void** proc,
        byte** errorMessage);


    [DllImport(DllName, EntryPoint = "mrbcs_release_proc", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int MrbcsReleaseProc(
        MrbStateNative* mrb,
        byte* source,
        int sourceLength,
        void** proc,
        byte** errorMessage);
}
