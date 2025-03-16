using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MRubyD.Compiler;

[StructLayout(LayoutKind.Sequential)]
struct MrbStateNative;

[StructLayout(LayoutKind.Explicit)]
unsafe struct MrbIrepPoolNative
{
    public const uint TT_STR = 0;
    public const uint TT_SSTR = 2;
    public const uint TT_INT32 = 1;
    public const uint TT_INT64 = 3;
    public const uint TT_BIGINT = 7;
    public const uint TT_FLOAT = 5;

    [FieldOffset(0)]
    public uint tt;

    [FieldOffset(4)]
    public byte* str;

    [FieldOffset(4)]
    public int i32;

    [FieldOffset(4)]
    public long i64;

    [FieldOffset(4)]
    public double f;
}

[StructLayout(LayoutKind.Sequential)]
unsafe struct MrbIrepCatchHandlerNative
{
    public byte type;
    public fixed byte begin[4];
    public fixed byte end[4];
    public fixed byte target[4];
}

[StructLayout(LayoutKind.Sequential)]
unsafe struct MrbIrepNative
{
    public ushort nlocals;
    public ushort nargs;
    public ushort clen;
    public byte flags;
    public byte* iseq;

    public MrbIrepPoolNative* pool;
    public Symbol* syms;
    public MrbIrepNative* reps;
    public Symbol* lv;
    public void* debug_info;

    public uint ilen;
    public ushort plen;
    public ushort slen;
    public ushort rlen;
}

[StructLayout(LayoutKind.Sequential)]
unsafe struct MrbdCompileHandleNative
{
    public int Id;
    public MrbIrepNative* Irep;
    public byte* ErrorMessage;
}

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

    [DllImport(DllName, EntryPoint = "mrbd_compile", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int MrbdCompile(
        MrbStateNative* mrb,
        byte* source,
        int sourceLength,
        byte** bin,
        int* binLength,
        byte** errorMessage);
}
