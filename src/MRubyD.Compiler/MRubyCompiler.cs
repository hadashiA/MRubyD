using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MRubyD.Compiler;

public class MRubyCompileException(string message) : Exception(message);

// public static class MRubyStateExtensions
// {
//     public static MRubyValue Evaluate(this MRubyState state, ReadOnlySpan<char> rubySourceCode)
//     {
//
//     }
// }

public record MRubyCompileOptions
{
    public static MRubyCompileOptions Default { get; set; } = new();
}

public class MRubyCompiler : IDisposable
{
    public static MRubyCompiler Create(MRubyState mrb, MRubyCompileOptions? options = null)
    {
        var compilerStateHandle = MrbStateHandle.Create();
        return new MRubyCompiler(mrb, compilerStateHandle, options);
    }

    readonly MRubyState mruby;
    readonly RiteParser riteParser;
    readonly MrbStateHandle compileStateHandle;
    readonly MRubyCompileOptions options;

    MRubyCompiler(
        MRubyState mruby,
        MrbStateHandle compileStateHandle,
        MRubyCompileOptions? options = null)
    {
        this.mruby = mruby;
        this.compileStateHandle = compileStateHandle;
        this.options = options ?? MRubyCompileOptions.Default;
        riteParser = new RiteParser(mruby);
    }

    public MRubyValue LoadExecFile(string path)
    {
        return mruby.Exec(CompileFile(path));
    }

    public MRubyValue LoadExec(ReadOnlySpan<byte> code)
    {
        return mruby.Exec(Compile(code));
    }

    public Irep CompileFile(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        return Compile(bytes);
    }

    public unsafe Irep Compile(ReadOnlySpan<byte> code)
    {
        var mrbPtr = compileStateHandle.DangerousGetPtr();
        byte* bin = null;
        var binLength = 0;
        byte* errorMessageCStr = null;
        int resultCode;
        fixed (byte* codePtr = code)
        {
            resultCode = NativeMethods.MrbdCompile(
                mrbPtr,
                codePtr,
                code.Length,
                &bin,
                &binLength,
                &errorMessageCStr);
        }

        try
        {
            if (resultCode != NativeMethods.Ok)
            {
                if (errorMessageCStr != null)
                {
                    var errorMessage = Marshal.PtrToStringUTF8((IntPtr)errorMessageCStr)!;
                    throw new MRubyCompileException(errorMessage);
                }
            }
            var span = new ReadOnlySpan<byte>(bin, binLength);
            return riteParser.Parse(span);
        }
        finally
        {
            if (bin != null)
            {
                NativeMethods.MrbFree(mrbPtr, bin);
            }
        }
    }

    public void Dispose()
    {
        compileStateHandle.Dispose();
        GC.SuppressFinalize(this);
    }

//     unsafe Irep GetIrepFromNative(MrbIrepNative* irepNative)
//     {
//         var sequenceLength = irepNative->ilen + irepNative->clen * 13; // TODO:
//         var sequence = new byte[sequenceLength];
//         fixed (byte* dst = sequence)
//         {
//             Buffer.MemoryCopy(irepNative->iseq, dst, sequenceLength, sequenceLength);
//         }
//
//         var symbolsLength = irepNative->slen;
//         var symbols = new Symbol[symbolsLength];
//         fixed (Symbol* dst = symbols)
//         {
//             Buffer.MemoryCopy(irepNative->syms, dst, symbolsLength, symbolsLength);
//         }
//
//         var localVariablesLength = irepNative->nlocals;
//         var localVariables = new Symbol[localVariablesLength];
//         fixed (Symbol* dst = localVariables)
//         {
//             Buffer.MemoryCopy(irepNative->lv, dst, localVariablesLength, localVariablesLength);
//         }
//
//         var childCount = irepNative->rlen;
//         var children = new Irep[childCount];
//         for (var i = 0; i < childCount; i++)
//         {
//             children[i] = GetIrepFromNative(irepNative->reps + i);
//         }
//
//         var poolValueCount = irepNative->plen;
//         var poolValues = new IrepPoolValue[poolValueCount];
//         for (var i = 0; i < poolValueCount; i++)
//         {
//             var poolNative = irepNative->pool + i;
//             switch (poolNative->tt)
//             {
//                 case MrbIrepPoolNative.TT_INT32:
//                     poolValues[i] = new IrepPoolValue(poolNative->i32);
//                     break;
//                 case MrbIrepPoolNative.TT_INT64:
//                     poolValues[i] = new IrepPoolValue(poolNative->i64);
//                     break;
//                 case MrbIrepPoolNative.TT_BIGINT:
//                     throw new NotSupportedException();
//                 case MrbIrepPoolNative.TT_FLOAT:
//                     poolValues[i] = new IrepPoolValue(poolNative->f);
//                     break;
//                 default:
//                     if ((poolNative->tt & MrbIrepPoolNative.TT_STR) != 0 ||
//                         (poolNative->tt & MrbIrepPoolNative.TT_SSTR) != 0)
//                     {
//                         var length = poolNative->tt >> 2;
//                         var span = new ReadOnlySpan<byte>(poolNative->str, (int)length);
//                         poolValues[i] = new IrepPoolValue(span.ToArray());
//                     }
//                     break;
//             }
//         }
//
//         var catchHandlerCount = irepNative->clen;
//         var catchHandlers = new CatchHandler[catchHandlerCount];
//         if (catchHandlerCount > 0)
//         {
//             var catchHandlerPtr = (MrbIrepCatchHandlerNative*)(irepNative->iseq + irepNative->ilen);
//             for (var i = 0; i < catchHandlerCount; i++)
//             {
//                 var ptr = catchHandlerPtr + i;
//                 var type = (CatchHandlerType)ptr->type;
//                 var begin = BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(ptr->begin, sizeof(uint)));
//                 var end = BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(ptr->end, sizeof(uint)));
//                 var target = BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(ptr->target, sizeof(uint)));
//                 catchHandlers[i] = new CatchHandler(type, begin, end, target);
//             }
//         }
//
//         return new Irep
//         {
//             Sequence = sequence,
//             Symbols = symbols,
//             LocalVariables = localVariables,
//             Children = children,
//             PoolValues = poolValues,
//             CatchHandlers = catchHandlers,
//         };
//     }
}