using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MRubyD.Internals;

public enum CallerType
{
    /// <summary>
    /// Called method from mruby VM
    /// </summary>
    InVmLoop,

    /// <summary>
    /// Ignited mruby VM from C#
    /// </summary>
    VmExecuted,

    /// <summary>
    /// Called method from C#
    /// </summary>
    MethodCalled,

    /// <summary>
    /// Resumed by `Fiber.yield` (probabily the main call is `mrb_fiber_resume`)
    /// </summary>
    Resumed,
}

public enum FiberState
{
    Created,
    Running,
    Resumed,
    Suspended,
    Transferred,
    Terminated
}

struct MRubyCallInfo
{
    public const int CallMaxArgs = 15;
    static readonly int CallVarArgs = (CallMaxArgs << 4) | CallMaxArgs;

    internal static int CalculateBlockArgumentOffset(int argc, int kargc)
    {
        var n = argc;
        if (argc == CallMaxArgs) n = 1;
        if (kargc == CallMaxArgs) n += 1;
        else n += kargc * 2;
        return n + 1; // self + args + kargs
    }

    internal static int CalculateKeywordArgumentOffset(int argc, int kargc)
    {
         if (kargc == 0) return -1;
        return argc == CallMaxArgs ? 2 : argc + 1;
    }

    public int StackPointer;
    public RProc? Proc;
    public int ProgramCounter;
    public byte ArgumentCount;
    public byte KeywordArgumentCount;
    // for stacktrace..
    public CallerType CallerType;
    public ICallScope Scope;
    public Symbol MethodId;

    public bool ArgumentPacked => ArgumentCount >= CallMaxArgs;
    public bool KeywordArgumentPacked => KeywordArgumentCount >= CallMaxArgs;
    public int KeywordArgumentOffset => CalculateKeywordArgumentOffset(ArgumentCount, KeywordArgumentCount);
    public int BlockArgumentOffset => CalculateBlockArgumentOffset(ArgumentCount, KeywordArgumentCount);

    public int NumberOfRegisters
    {
        get
        {
            var numberOfRegisters = BlockArgumentOffset + 1; // self + args + kargs + blk
            if (Proc is IrepProc p && p.Irep.RegisterVariableCount > numberOfRegisters)
            {
                return p.Irep.RegisterVariableCount;
            }
            return numberOfRegisters;
        }
    }

    public void Clear()
    {
        // Proc?.SetFlag(MRubyObjectFlags.ProcOrphan);
        Proc = null;
        Scope = null!;
        MethodId = default;
        ArgumentCount = 0;
        KeywordArgumentCount = 0;
    }

    public void MarkAsArgumentPacked()
    {
        ArgumentCount = CallMaxArgs;
    }

    public void MarkAsKeywordArgumentPacked()
    {
        KeywordArgumentCount = CallMaxArgs;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadOperand(ReadOnlySpan<byte> sequence, out short operand1)
    {
        var pc = ProgramCounter;
        operand1 = BinaryPrimitives.ReadInt16BigEndian(sequence[(pc + 1)..]);
        ProgramCounter += 3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadOperand(ReadOnlySpan<byte> sequence, out byte operand1)
    {
        var pc = ProgramCounter;
        operand1 = sequence[pc + 1];
        ProgramCounter += 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadOperand(ReadOnlySpan<byte> sequence, out byte operand1, out byte operand2)
    {
        var pc = ProgramCounter;
        operand1 = sequence[pc + 1];
        operand2 = sequence[pc + 2];
        ProgramCounter += 3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadOperand(ReadOnlySpan<byte> sequence, out byte operand1, out byte operand2, out byte operand3)
    {
        var pc = ProgramCounter;
        operand1 = sequence[pc + 1];
        operand2 = sequence[pc + 2];
        operand3 = sequence[pc + 3];
        ProgramCounter += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadOperand(ReadOnlySpan<byte> sequence, out byte operand1, out short operand2)
    {
        var pc = ProgramCounter;
        operand1 = sequence[pc + 1];
        operand2 = BinaryPrimitives.ReadInt16BigEndian(sequence[(pc + 2)..]);
        ProgramCounter += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadOperand(ReadOnlySpan<byte> sequence, out short operand1, out byte operand2)
    {
        var pc = ProgramCounter;
        operand1 = BinaryPrimitives.ReadInt16BigEndian(sequence[(pc + 1)..]);
        operand2 = sequence[pc + 3];
        ProgramCounter += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadOperand(ReadOnlySpan<byte> sequence, out byte operand1, out int operand2)
    {
        var pc = ProgramCounter;
        operand1 = sequence[pc + 1];
        operand2 = BinaryPrimitives.ReadInt32BigEndian(sequence[(pc + 2)..]);
        ProgramCounter += 6;
    }
}

class MRubyContext
{
    const int CallStackInitSize = 128;
    const int StackInitSize = 32;
    const int CallDepthMax = 512;

    public int CallDepth { get; private set; }

    public MRubyContext? Previous;
    internal MRubyValue[] Stack  = new MRubyValue[StackInitSize];
    internal MRubyCallInfo[] CallStack = new MRubyCallInfo[CallStackInitSize];
    // public FiberState FiberState;
    // public RFiber? fiber;

    public ref MRubyCallInfo CurrentCallInfo
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref CallStack[CallDepth];
    }

    public Span<MRubyValue> CurrentStack
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var sp = CallStack[CallDepth].StackPointer;
            return Stack.AsSpan(sp);
        }
    }

    public MRubyContext()
    {
        CallStack[0] = new MRubyCallInfo();
    }

    public bool CheckProcIsOrphan(RProc proc)
    {
        if (proc.Scope is REnv procEnv)
        {
            if (CallDepth > 0)
            {
                return CallStack[CallDepth - 1].Scope == procEnv;
            }
        }
        return false;
    }

    public ref MRubyCallInfo PushCallStack()
    {
        EnsureStackLevel();

        if (CallStack.Length <= CallDepth + 1)
        {
            Array.Resize(ref CallStack, CallStack.Length * 2);
        }
        return ref CallStack[++CallDepth];
    }

    public void PopCallStack()
    {
        if (CallDepth <= 0)
        {
            throw new InvalidOperationException();
        }

        ref var currentCallInfo = ref CallStack[CallDepth];
        ref var parentCallInfo = ref CallStack[CallDepth - 1];

        var currentBlock = Stack[currentCallInfo.BlockArgumentOffset];
        if (currentBlock.Object is RProc b &&
            !b.HasFlag(MRubyObjectFlags.ProcStrict) &&
            b.Scope == parentCallInfo.Scope)
        {
            b.SetFlag(MRubyObjectFlags.ProcOrphan);
        }

        if (currentCallInfo.Scope is REnv currentEnv)
        {
            currentEnv.CaptureStack();
        }

        // currentCallInfo.
        CallStack[CallDepth].Clear();
        CallDepth--;
    }

    public void UnwindStack(int to)
    {
        if ((uint)to >= (uint)Stack.Length)
        {
            throw new ArgumentOutOfRangeException();
        }
        CallDepth = to;
    }

    public Memory<MRubyValue> CaptureStack(int stackPointer)
    {
        return Stack.AsMemory(stackPointer);
    }

    public bool IsRecursiveCalling(RObject receiver, Symbol methodId, int offset = 0)
    {
        for (var i = CallDepth - 1 - offset; i >= 0; i--)
        {
            ref var callInfo = ref CallStack[i];
            if (callInfo.MethodId == methodId &&
                Stack[callInfo.StackPointer].Object == receiver)
            {
                return true;
            }
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ExtendStack(int room)
    {
        if (Stack.Length <= room)
        {
            var newSize = Math.Max(128, Math.Max(Stack.Length * 2, room));
            Array.Resize(ref Stack, newSize);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearStack(int start, int count)
    {
        if (count <= 0) return;
        Stack.AsSpan(start, count).Clear();
    }

    public int GetArgumentCount()
    {
        ref var callInfo = ref CallStack[CallDepth];
        if (callInfo.ArgumentPacked)
        {
            return Stack[callInfo.StackPointer + 1].As<RArray>().Length;
        }
        return callInfo.ArgumentCount;
    }

    public int GetKeywordArgumentCount()
    {
        ref var callInfo = ref CallStack[CallDepth];
        var offset = callInfo.KeywordArgumentOffset;
        if (callInfo.KeywordArgumentPacked)
        {
            return Stack[callInfo.StackPointer + offset].As<RHash>().Length;
        }
        return callInfo.KeywordArgumentCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue GetArg(int index)
    {
        ref var callInfo = ref CallStack[CallDepth];
        var arg = Stack[callInfo.StackPointer + 1 + index];
        return arg;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue GetKeywordArg(Symbol key)
    {
        ref var callInfo = ref CallStack[CallDepth];
        var offset = callInfo.KeywordArgumentOffset;
        if (offset < 0)
        {
            return MRubyValue.Nil;
        }

        if (callInfo.KeywordArgumentPacked)
        {
            var hash = Stack[callInfo.StackPointer + offset].As<RHash>();
            return hash[MRubyValue.From(key)];
        }

        for (var i = 0; i < callInfo.KeywordArgumentCount; i++)
        {
            var k = Stack[callInfo.StackPointer + offset + i];
            if (k.SymbolValue == key)
            {
                return Stack[callInfo.StackPointer + offset + i + 1];
            }
        }
        return MRubyValue.Nil;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<MRubyValue> GetRestArg(int startIndex) =>
        GetRestArg(ref CurrentCallInfo, startIndex);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue GetBlockArg()
    {
        ref var callInfo = ref CurrentCallInfo;
        return Stack[callInfo.StackPointer + callInfo.BlockArgumentOffset];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Span<MRubyValue> GetRestArg(ref MRubyCallInfo callInfo, int startIndex)
    {
        if (callInfo.ArgumentPacked)
        {
            return Stack[callInfo.StackPointer + 1].As<RArray>().AsSpan();
        }
        return Stack.AsSpan(callInfo.StackPointer + 1 + startIndex, callInfo.ArgumentCount - startIndex);
    }

    internal ReadOnlySpan<KeyValuePair<Symbol, MRubyValue>> GetKeywordArgs(ref MRubyCallInfo callInfo)
    {
        var offset = callInfo.KeywordArgumentOffset;
        if (offset < 0)
        {
            return [];
        }

        var list = new List<KeyValuePair<Symbol, MRubyValue>>();
        if (callInfo.KeywordArgumentPacked)
        {
            var kdict = Stack[callInfo.StackPointer + offset].As<RHash>();
            foreach (var (k, v) in kdict)
            {
                list.Add(new KeyValuePair<Symbol, MRubyValue>(k.SymbolValue, v));
            }
        }
        else
        {
            for (var i = 0; i < callInfo.KeywordArgumentCount; i++)
            {
                var k = Stack[callInfo.StackPointer + offset + i * 2];
                var v = Stack[callInfo.StackPointer + offset + i * 2 + 1];
                list.Add(new KeyValuePair<Symbol, MRubyValue>(k.SymbolValue, v));
            }
        }
        return CollectionsMarshal.AsSpan(list);
    }

    void EnsureStackLevel()
    {
        if (CallDepth >= CallDepthMax)
        {
            throw new InvalidOperationException("stack level too deep");
        }
    }
}

