//#define CASE_MARKER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MRubyCS.Internals;
using MRubyCS.StdLib;

namespace MRubyCS;

partial class MRubyState
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsRecursiveCalling(RObject receiver, Symbol methodId) =>
        context.IsRecursiveCalling(receiver, methodId);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetArgumentCount() => context.GetArgumentCount();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetKeywordArgumentCount() => context.GetKeywordArgumentCount();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue GetArg(int index) => context.GetArg(index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MRubyValue GetKeywordArg(Symbol key) => context.GetKeywordArg(key);

    public bool TryGetArg(int index, out MRubyValue value) => context.TryGetArg(index, out value);
    public bool TryGetKeywordArg(Symbol key, out MRubyValue value) => context.TryGetKeywordArg(key, out value);
    public MRubyValue GetSelf() => context.GetSelf();

    public ReadOnlySpan<KeyValuePair<Symbol, MRubyValue>> GetKeywordArgs() =>
        context.GetKeywordArgs(ref context.CurrentCallInfo);

    public RClass GetArgAsClass(int index)
    {
        var arg = GetArg(index);
        EnsureValueType(arg, MRubyVType.Class);
        return arg.As<RClass>();
    }

    public Symbol GetArgAsSymbol(int index)
    {
        var arg = GetArg(index);
        return ToSymbol(arg);
    }

    public long GetArgAsInteger(int index)
    {
        var arg = GetArg(index);
        return ToInteger(arg);
    }

    public double GetArgAsFloat(int index)
    {
        var arg = GetArg(index);
        return ToFloat(arg);
    }

    public RString GetArgAsString(int index)
    {
        var arg = GetArg(index);
        if (arg.VType != MRubyVType.String)
        {
            Raise(Names.TypeError, NewString($"{StringifyAny(arg)} cannot be converted to String"));
        }
        return arg.As<RString>();
    }

    public ReadOnlySpan<MRubyValue> GetRestArg(int startIndex) =>
        context.GetRestArg(startIndex);

    public MRubyValue GetBlockArg(bool optional = true)
    {
        var blockArg = context.GetBlockArg();
        if (!optional && blockArg.IsNil)
        {
            Raise(Names.ArgumentError, "no block given"u8);
        }
        return blockArg;
    }

    public MRubyValue Send(MRubyValue self, Symbol methodId) =>
        Send(self, methodId, ReadOnlySpan<MRubyValue>.Empty);

    public MRubyValue Send(MRubyValue self, Symbol methodId, params ReadOnlySpan<MRubyValue> args) =>
        Send(self, methodId, args, null, null);

    public MRubyValue Send(
        MRubyValue self,
        Symbol methodId,
        RProc block) =>
        Send(self, methodId, ReadOnlySpan<MRubyValue>.Empty, null, block);

    public MRubyValue Send(
        MRubyValue self,
        Symbol methodId,
        ReadOnlySpan<MRubyValue> args,
        ReadOnlySpan<KeyValuePair<Symbol, MRubyValue>> kargs,
        RProc? block)
    {
        ref var currentCallInfo = ref context.CurrentCallInfo;
        var nextStackPointer = currentCallInfo.StackPointer + currentCallInfo.NumberOfRegisters;

        var stackSize = MRubyCallInfo.CalculateBlockArgumentOffset(
            args.Length,
            kargs.IsEmpty ? 0 : MRubyCallInfo.CallMaxArgs) + 1; // argc + kargs(packed) + self + proc
        context.ExtendStack(nextStackPointer + stackSize);

        var nextStack = context.Stack.AsSpan(nextStackPointer);

        var receiverClass = ClassOf(self);
        ref var nextCallInfo = ref context.PushCallStack();
        nextCallInfo.StackPointer = nextStackPointer;
        nextCallInfo.Scope = receiverClass;
        nextCallInfo.ArgumentCount = (byte)args.Length;
        nextCallInfo.KeywordArgumentCount = (byte)kargs.Length;

        nextStack[0] = self;
        if (!args.IsEmpty)
        {
            // packing
            if (args.Length >= MRubyCallInfo.CallMaxArgs)
            {
                throw new NotImplementedException();
            }
            else
            {
                args.CopyTo(nextStack[1..]);
            }
        }

        if (!kargs.IsEmpty)
        {
            var kargOffset = MRubyCallInfo.CalculateKeywordArgumentOffset(args.Length, kargs.Length);
            // packing
            var kdict = NewHash(kargs.Length);
            foreach (var (key, value) in kargs)
            {
                kdict.Add(MRubyValue.From(key), value);
            }
            nextStack[kargOffset] = MRubyValue.From(kdict);
            nextCallInfo.MarkAsKeywordArgumentPacked();
        }
        nextStack[stackSize - 1] = block != null ? MRubyValue.From(block) : MRubyValue.Nil;

        if (TryFindMethod(receiverClass, methodId, out var method, out _))
        {
            nextCallInfo.MethodId = methodId;
        }
        else
        {
            method = PrepareMethodMissing(ref nextCallInfo, self, methodId);
        }
        nextCallInfo.Proc = method.Proc;

        // var block = stack[blockArgumentOffset];
        // if (!block.IsNil) EnsureValueIsBlock(block);

        if (method.Kind == MRubyMethodKind.CSharpFunc)
        {
            nextCallInfo.CallerType = CallerType.MethodCalled;
            nextCallInfo.ProgramCounter = 0;

            var result = method.Invoke(this, self);
            context.PopCallStack();
            return result;
        }
        else
        {
            var irepProc = nextCallInfo.Proc!;
            nextCallInfo.CallerType = CallerType.VmExecuted;
            nextCallInfo.ProgramCounter = irepProc.ProgramCounter;
            return Exec(irepProc.Irep, irepProc.ProgramCounter, nextCallInfo.BlockArgumentOffset + 1);
        }
    }

    public MRubyValue YieldWithClass(
        RClass c,
        MRubyValue self,
        ReadOnlySpan<MRubyValue> args,
        RProc block)
    {
        ref var callInfo = ref context.CurrentCallInfo;

        var stackSize = callInfo.NumberOfRegisters;
        ref var nextCallInfo = ref context.PushCallStack();
        nextCallInfo.StackPointer = callInfo.StackPointer + stackSize;
        nextCallInfo.CallerType = CallerType.VmExecuted;
        nextCallInfo.MethodId = block.Scope is REnv env
            ? env.MethodId
            : callInfo.MethodId;
        nextCallInfo.Proc = block;
        nextCallInfo.Scope = c;

        var nextStack = context.Stack.AsSpan(nextCallInfo.StackPointer);
        nextStack[0] = self;

        if (args.Length > MRubyCallInfo.CallMaxArgs)
        {
            // TODO: packing
            throw new NotImplementedException();
        }
        else
        {
            args.CopyTo(nextStack[1..]);
            nextCallInfo.ArgumentCount = (byte)args.Length;
        }
        nextCallInfo.KeywordArgumentCount = 0;

        if (block is not { } proc)
        {
            throw new NotSupportedException();
        }

        return Exec(proc.Irep, proc.ProgramCounter, nextCallInfo.BlockArgumentOffset + 1);
    }

    public MRubyValue Exec(ReadOnlySpan<byte> bytecode)
    {
        riteParser ??= new RiteParser(this);
        var irep = riteParser.Parse(bytecode);
        return Exec(irep);
    }

    public MRubyValue Exec(Irep irep)
    {
        var proc = new RProc(irep, 0, ProcClass)
        {
            Upper = null,
            Scope = ObjectClass
        };

        context.UnwindStack();

        ref var callInfo = ref context.CurrentCallInfo;
        callInfo.StackPointer = 0;
        callInfo.Proc = proc;
        callInfo.Scope = ObjectClass;
        callInfo.MethodId = default;
        callInfo.CallerType = CallerType.VmExecuted;
        context.Stack[0] = MRubyValue.From(TopSelf);
        return Exec(irep, 0, 1);
    }

    public string GetBacktraceString()
    {
        var backtrace = Backtrace.Capture(context);
        return backtrace.ToString(this);
    }

    internal bool CheckProcIsOrphan(RProc proc) =>
        context.CheckProcIsOrphan(proc);

    internal MRubyValue SendMeta(MRubyValue self)
    {
        // ref var callInfo = ref context.CurrentCallInfo;

        var methodId = GetArgAsSymbol(0);
        // if (callInfo.CallerType != CallerType.InVmLoop)
        {
            var block = GetBlockArg();
            var args = GetRestArg(1);
            var kargs = GetKeywordArgs();
            return Send(self, methodId, args, kargs, block.IsNil ? null : block.As<RProc>());
        }

        // var registers = context.Stack.AsSpan(callInfo.StackPointer + 1);
        // var c = ClassOf(self);
        // if (!TryFindMethod(c, methodId, out var method, out _) || method == MRubyMethod.Nop)
        // {
        //     throw new NotImplementedException();
        // }
        //
        // callInfo.MethodId = methodId;
        // callInfo.Scope = c;

    }

    internal MRubyValue EvalUnder(MRubyValue self, RProc block, RClass c)
    {
        ref var callInfo = ref context.CurrentCallInfo;
        if (callInfo.CallerType == CallerType.MethodCalled)
        {
            return YieldWithClass(c, self, [self], block);
        }

        callInfo.Scope = c;
        callInfo.Proc = block;
        callInfo.ProgramCounter = block.ProgramCounter;
        callInfo.ArgumentCount = 0;
        callInfo.KeywordArgumentCount = 0;
        callInfo.MethodId = context.CallStack[context.CallDepth - 1].MethodId;

        var nregs = block.Irep.RegisterVariableCount < 4 ? 4 : block.Irep.RegisterVariableCount;
        context.ExtendStack(nregs);
        context.Stack[callInfo.StackPointer] = self;
        context.Stack[callInfo.StackPointer + 1] = self;
        context.ClearStack(callInfo.StackPointer + 2, nregs - 2);

        // Popped at the end of an upstream method call such as instance_eval/class_eval, and the above rewritten callInfo is executed.
        context.PushCallStack();
        return self;
    }

    /// <summary>
    /// Execute irep assuming the Stack values are placed
    /// </summary>
    MRubyValue Exec(Irep irep, int pc, int stackKeep)
    {
        Exception = null;

        var registerVariableCount = irep.RegisterVariableCount;
        if (stackKeep > registerVariableCount)
        {
            registerVariableCount = (ushort)stackKeep;
        }

        ReadOnlySpan<byte> sequence = irep.Sequence.AsSpan(pc);

        ref var callInfo = ref context.CurrentCallInfo;
        context.ExtendStack(callInfo.StackPointer + registerVariableCount);
        context.ClearStack(callInfo.StackPointer + stackKeep, registerVariableCount - stackKeep);

        var registers = context.Stack.AsSpan(callInfo.StackPointer);
        callInfo.ProgramCounter = pc;

        while (true)
        {
            try
            {
                var opcode = (OpCode)sequence[callInfo.ProgramCounter];
                switch (opcode)
                {
                    case OpCode.Nop:
                        Markers.Nop();
                    {
                        callInfo.ProgramCounter++;
                        goto Next;
                    }
                    case OpCode.Move:
                        Markers.Move();
                        var bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        registers[bb.A] = registers[bb.B];
                        goto Next;
                    case OpCode.LoadL:
                        Markers.LoadL();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        registers[bb.A] = irep.PoolValues[bb.B];
                        goto Next;
                    case OpCode.LoadI8:
                       Markers.LoadI8();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        registers[bb.A] = MRubyValue.From(bb.B);
                        goto Next;
                    case OpCode.LoadINeg:
                        Markers.LoadINeg();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        registers[bb.A] = MRubyValue.From(-bb.B);
                        goto Next;
                    case OpCode.LoadI__1:
                    case OpCode.LoadI_0:
                    case OpCode.LoadI_1:
                    case OpCode.LoadI_2:
                    case OpCode.LoadI_3:
                    case OpCode.LoadI_4:
                    case OpCode.LoadI_5:
                    case OpCode.LoadI_6:
                    case OpCode.LoadI_7:
                        Markers.LoadI__1();
                        int a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        registers[a] = MRubyValue.From((int)opcode - (int)OpCode.LoadI_0);
                        goto Next;
                    case OpCode.LoadI16:
                        Markers.LoadI16();
                        var bs = OperandBS.Read(sequence, ref callInfo.ProgramCounter);
                        registers[bs.A] = MRubyValue.From(bs.B);
                        goto Next;
                    case OpCode.LoadI32:
                        Markers.LoadI32();
                        var bss = OperandBSS.Read(sequence, ref callInfo.ProgramCounter);
                        registers[bss.A] = MRubyValue.From((bss.B << 16) + bss.C);
                        goto Next;
                    case OpCode.LoadSym:
                        Markers.LoadSym();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        registers[bb.A] = MRubyValue.From(irep.Symbols[bb.B]);
                        goto Next;
                    case OpCode.LoadNil:
                        Markers.LoadNil();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        registers[a] = default;
                        goto Next;
                    case OpCode.LoadSelf:
                        Markers.LoadSelf();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        registers[a] = registers[0];
                        goto Next;
                    case OpCode.LoadT:
                        Markers.LoadT();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        registers[a] = MRubyValue.True;
                        goto Next;
                    case OpCode.LoadF:
                        Markers.LoadF();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        registers[a] = MRubyValue.False;
                        goto Next;
                    case OpCode.GetGV:
                        Markers.GetGV();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        registers[bb.A] = globalVariables.Get(irep.Symbols[bb.B]);
                        goto Next;
                    case OpCode.SetGV:
                        Markers.SetGV();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        globalVariables.Set(irep.Symbols[bb.B], registers[bb.A]);
                        goto Next;
                    case OpCode.GetSV:
                    case OpCode.SetSV:
                        Markers.GetSV();
                        callInfo.ProgramCounter += 3;
                        goto Next;
                    case OpCode.GetIV:
                        Markers.GetIV();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        registers[bb.A] = registers[0].As<RObject>().InstanceVariables.Get(irep.Symbols[bb.B]);
                        goto Next;
                    case OpCode.SetIV:
                        Markers.SetIV();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        registers[0].As<RObject>().InstanceVariables.Set(irep.Symbols[bb.B], registers[bb.A]);
                        goto Next;
                    case OpCode.GetCV:
                        Markers.GetCV();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        registers[bb.A] = GetClassVariable(irep.Symbols[bb.B]);
                        goto Next;
                    case OpCode.SetCV:
                        Markers.SetCV();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        SetClassVariable(irep.Symbols[bb.B], registers[bb.A]);
                        goto Next;
                    
                    case OpCode.GetConst:
                        Markers.GetConst();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        ref var registerA = ref registers[bb.A];
                    {
                        var id = irep.Symbols[bb.B];
                        var c = callInfo.Proc?.Scope?.TargetClass ?? ObjectClass;
                        if (c.InstanceVariables.TryGet(id, out var value))
                        {
                            registerA = value;
                            goto Next;
                        }

                        var x = c;
                        while (x is { VType: MRubyVType.SClass })
                        {
                            if (!x.InstanceVariables.TryGet(id, out value))
                            {
                                x = null;
                                break;
                            }
                            x = c.Class;
                        }
                        if (x is { VType: MRubyVType.Class or MRubyVType.Module })
                        {
                            c = x;
                        }
                        var proc = callInfo.Proc?.Upper;
                        while (proc != null)
                        {
                            x = proc.Scope?.TargetClass ?? ObjectClass;
                            if (x.InstanceVariables.TryGet(id, out value))
                            {
                                registerA = value;
                                goto Next;
                            }
                            proc = proc.Upper;
                        }
                        registerA = GetConst(id, c);
                        goto Next;
                    }
                    case OpCode.SetConst:
                    {
                        Markers.SetConst();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        //var id = irep.Symbols[bb.B];
                        var c = callInfo.Proc?.Scope?.TargetClass ?? ObjectClass;
                        SetConst(irep.Symbols[bb.B], c, registers[bb.A]);
                        goto Next;
                    }
                    case OpCode.GetMCnst:
                        Markers.GetMCnst();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        registerA = ref registers[bb.A];
                    {
                        
                        //var mod = registers[bb.A];
                        var name = irep.Symbols[bb.B];
                        registerA = GetConst(name, registerA.As<RClass>());
                        goto Next;
                    }
                    case OpCode.SetMCnst:
                    {
                        Markers.SetMCnst();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        registerA = ref registers[bb.A];
                        //var mod = registers[bb.A + 1];
                        var name = irep.Symbols[bb.B];
                        SetConst(name, Unsafe.Add(ref registerA, 1).As<RClass>(), registerA);
                        goto Next;
                    }
                    case OpCode.GetIdx:
                    {
                        Markers.GetIdx();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        registerA = ref registers[a];
                        var valueB = Unsafe.Add(ref registerA, 1);
                        switch (registerA.Object)
                        {
                            case RArray array when valueB.IsInteger:
                                registerA = array[(int)valueB.IntegerValue];
                                goto Next;
                            case RHash hash:
                                registerA = hash[valueB];
                                goto Next;
                            case RString str:
                                switch (valueB.VType)
                                {
                                    case MRubyVType.Integer:
                                    case MRubyVType.String:
                                    case MRubyVType.Range:
                                        var substr = str.GetAref(valueB);
                                        registerA = substr != null
                                            ? MRubyValue.From(substr)
                                            : MRubyValue.Nil;
                                    break;
                                }

                            break;
                        }

                        // Jump to send :[]
                        Unsafe.Add(ref registerA, 2) = MRubyValue.Nil; // push nil after arguments
                        var nextStackPointer = callInfo.StackPointer + a;
                        callInfo = ref context.PushCallStack();
                        callInfo.CallerType = CallerType.InVmLoop;
                        callInfo.StackPointer = nextStackPointer;
                        callInfo.MethodId = Names.OpAref;
                        callInfo.ArgumentCount = 1;
                        callInfo.KeywordArgumentCount = 0;
                        goto case OpCode.SendInternal;
                    }
                    case OpCode.SetIdx:
                    {
                        Markers.SetIdx();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        registers[a + 3] = MRubyValue.Nil; // push nil after arguments

                        // Jump to send :[]=
                        var nextStackPointer = callInfo.StackPointer + a;
                        callInfo = ref context.PushCallStack();
                        callInfo.CallerType = CallerType.InVmLoop;
                        callInfo.StackPointer = nextStackPointer;
                        callInfo.MethodId = Names.OpAset;
                        callInfo.ArgumentCount = 2;
                        callInfo.KeywordArgumentCount = 0;
                        goto case OpCode.SendInternal;
                    }
                    case OpCode.GetUpVar:
                        Markers.GetUpVar();
                        OperandBBB bbb;
                    {
                        bbb = OperandBBB.Read(sequence, ref callInfo.ProgramCounter);
                        var env = callInfo.Proc?.FindUpperEnvTo(bbb.C);
                        if (env != null && bbb.B < env.Stack.Length)
                        {
                            registers[bbb.A] = env.Stack.Span[bbb.B];
                        }
                        else
                        {
                            registers[bbb.A] = MRubyValue.Nil;
                        }
                        goto Next;
                    }
                    case OpCode.SetUpVar:
                    {
                        Markers.SetUpVar();
                        bbb = OperandBBB.Read(sequence, ref callInfo.ProgramCounter);
                        var env = callInfo.Proc?.FindUpperEnvTo(bbb.C);
                        if (env != null && bbb.B < env.Stack.Length)
                        {
                            env.Stack.Span[bbb.B] = registers[bbb.A];
                        }
                        goto Next;
                    }
                    case OpCode.Jmp:
                        Markers.Jmp();
                        var s = ReadOperandS(sequence, ref callInfo.ProgramCounter);
                        callInfo.ProgramCounter += s;
                        goto Next;
                    case OpCode.JmpIf:
                        Markers.JmpIf();
                        bs = OperandBS.Read(sequence, ref callInfo.ProgramCounter);
                        if (registers[bs.A].Truthy)
                        {
                            callInfo.ProgramCounter += bs.B;
                        }
                        goto Next;
                    case OpCode.JmpNot:
                        Markers.JmpNot();
                        bs = OperandBS.Read(sequence, ref callInfo.ProgramCounter);
                        if (registers[bs.A].Falsy)
                        {
                            callInfo.ProgramCounter += bs.B;
                        }
                        goto Next;
                    case OpCode.JmpNil:
                        Markers.JmpNil();
                        bs = OperandBS.Read(sequence, ref callInfo.ProgramCounter);
                        if (registers[bs.A].IsNil)
                        {
                            callInfo.ProgramCounter += bs.B;
                        }
                        goto Next;
                    case OpCode.JmpUw:
                    {
                        Markers.JmpUw();
                        s = ReadOperandS(sequence, ref callInfo.ProgramCounter);
                        var newProgramCounter = callInfo.ProgramCounter + s;
                        if (irep.TryFindCatchHandler(callInfo.ProgramCounter, CatchHandlerType.Ensure, out var catchHandler))
                        {
                            // avoiding a jump from a catch handler into the same handler
                            if (newProgramCounter < catchHandler.Begin ||
                                newProgramCounter > catchHandler.End)
                            {
                                PrepareTaggedBreak(BreakTag.Jump, context.CallDepth, MRubyValue.From(newProgramCounter));
                                callInfo.ProgramCounter = (int)catchHandler.Target;
                                goto Next;
                            }
                        }
                        Exception = null;
                        callInfo.ProgramCounter = newProgramCounter;
                        goto Next;
                    }
                    case OpCode.Except:
                        Markers.Except();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        registers[a] = Exception switch
                        {
                            MRubyRaiseException x => MRubyValue.From(x.ExceptionObject),
                            MRubyBreakException x => MRubyValue.From(x.BreakObject),
                            _ => MRubyValue.Nil
                        };
                        Exception = null;
                        goto Next;
                    case OpCode.Rescue:
                    {
                        Markers.Rescue();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        var exceptionObjectValue = registers[bb.A];
                        var exceptionClassValue = registers[bb.B];
                        switch (exceptionClassValue.VType)
                        {
                            case MRubyVType.Class:
                            case MRubyVType.Module:
                            break;
                            default:
                                var ex = new RException(
                                    NewString("class or module required for rescue clause"u8),
                                    GetExceptionClass(Names.TypeError));
                                Exception = new MRubyRaiseException(this, ex, context.CallDepth);
                                if (TryRaiseJump(ref callInfo))
                                {
                                    goto JumpAndNext;
                                }
                                throw Exception;
                        }

                        registers[bb.B] = MRubyValue.From(KindOf(exceptionObjectValue, exceptionClassValue.As<RClass>()));
                        goto Next;
                    }
                    case OpCode.RaiseIf:
                    {
                        Markers.RaiseIf();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        var exceptionValue = registers[a];
                        switch (exceptionValue.Object)
                        {
                            case RBreak breakObject:
                                Exception = new MRubyBreakException(this, breakObject);
                                switch (breakObject.Tag)
                                {
                                    case BreakTag.Break:
                                    {
                                        if (TryReturnJump(ref callInfo, breakObject.BreakIndex, breakObject.Value))
                                        {
                                            goto JumpAndNext;
                                        }
                                        return breakObject.Value;
                                    }
                                    case BreakTag.Jump:
                                    {
                                        var newProgramCounter = (int)breakObject.Value.IntegerValue;
                                        if (irep.TryFindCatchHandler(callInfo.ProgramCounter, CatchHandlerType.Ensure, out var catchHandler))
                                        {
                                            // avoiding a jump from a catch handler into the same handler
                                            if (newProgramCounter < catchHandler.Begin || newProgramCounter > catchHandler.End)
                                            {
                                                PrepareTaggedBreak(BreakTag.Jump, context.CallDepth, MRubyValue.From(newProgramCounter));
                                                callInfo.ProgramCounter = (int)catchHandler.Target;
                                                goto Next;
                                            }
                                        }
                                        Exception = null;
                                        callInfo.ProgramCounter = newProgramCounter;
                                        goto Next;
                                    }
                                    case BreakTag.Stop:
                                    {
                                        if (TryUnwindEnsureJump(ref callInfo, context.CallDepth, BreakTag.Stop, breakObject.Value))
                                        {
                                            goto JumpAndNext;
                                        }
                                        if (Exception != null) throw Exception;
                                        return registers[irep.LocalVariables.Length];
                                    }
                                }

                            break;
                            case RException exceptionObject:
                                Exception = new MRubyRaiseException(this, exceptionObject, context.CallDepth);
                                if (TryRaiseJump(ref callInfo))
                                {
                                    goto JumpAndNext;
                                }
                                throw Exception;
                            default:
                                Exception = null;
                            break;
                        }
                        goto Next;
                    }
                    case OpCode.SSend:
                    case OpCode.SSendB:
                    case OpCode.Send:
                    case OpCode.SendB:
                    {
                        Markers.SSend();
                        bbb = OperandBBB.Read(sequence, ref callInfo.ProgramCounter);
                        var currentStackPointer = callInfo.StackPointer;

                        callInfo = ref context.PushCallStack();
                        callInfo.CallerType = CallerType.InVmLoop;
                        callInfo.StackPointer = currentStackPointer + bbb.A;
                        callInfo.MethodId = irep.Symbols[bbb.B];
                        callInfo.ArgumentCount = (byte)(bbb.C & 0xf);
                        callInfo.KeywordArgumentCount = (byte)((bbb.C >> 4) & 0xf);

                        var nextRegisters = context.Stack.AsSpan(callInfo.StackPointer);
                        var blockOffset = callInfo.BlockArgumentOffset;
                        var kargOffset = callInfo.KeywordArgumentOffset;
                        if (callInfo.KeywordArgumentCount > 0)
                        {
                            if (callInfo.KeywordArgumentPacked)
                            {
                                var kdict = nextRegisters[kargOffset];
                                EnsureValueType(kdict, MRubyVType.Hash);
                            }
                            else
                            {
                                var hash = NewHash(callInfo.KeywordArgumentCount);
                                for (var i = 0; i < callInfo.KeywordArgumentCount; i++)
                                {
                                    var k = nextRegisters[kargOffset + (i * 2)];
                                    var v = nextRegisters[kargOffset + (i * 2) + 1];
                                    hash.Add(k, v);
                                }
                                nextRegisters[kargOffset] = MRubyValue.From(hash);

                                var block = nextRegisters[blockOffset];
                                callInfo.MarkAsKeywordArgumentPacked();
                                blockOffset = callInfo.BlockArgumentOffset;
                                nextRegisters[blockOffset] = block;
                            }
                        }

                        if (opcode is OpCode.Send or OpCode.SSend)
                        {
                            nextRegisters[blockOffset] = MRubyValue.Nil;
                        }
                        else
                        {
                            var block = nextRegisters[blockOffset];
                            if (!block.IsNil) EnsureValueIsBlock(block);
                        }

                        // self send
                        if (opcode is OpCode.SSend or OpCode.SSendB)
                        {
                            nextRegisters[0] = registers[0];
                        }
                        goto case OpCode.SendInternal;
                    }
                    case OpCode.SendInternal:
                    {
                        Markers.SendInternal();
                        var self = context.Stack[callInfo.StackPointer];
                        var receiverClass = opcode == OpCode.Super
                            ? (RClass)callInfo.Scope // set RClass.Super in OpCode.Super
                            : ClassOf(self);
                        var methodId = callInfo.MethodId;
                        if (!TryFindMethod(receiverClass, methodId, out var method, out _))
                        {
                            method = PrepareMethodMissing(ref callInfo, self, methodId,
                                opcode == OpCode.Super
                                    ? static (state, self, methodId) => state.Raise(Names.NoMethodError, state.NewString($"no superclass method '{state.NameOf(methodId)}' for {state.StringifyAny(self)}"))
                                    : null);
                        }
                        
                        callInfo.Scope = receiverClass;
                        callInfo.Proc = method.Proc;

                        // var block = stack[blockArgumentOffset];
                        // if (!block.IsNil) EnsureValueIsBlock(block);

                        if (method.Kind == MRubyMethodKind.CSharpFunc)
                        {
                            var result = method.Invoke(this, self);
                            context.Stack[callInfo.StackPointer] = result;

                            context.PopCallStack();
                            callInfo = ref context.CurrentCallInfo;
                            irep = callInfo.Proc!.Irep;
                            registers = context.Stack.AsSpan(callInfo.StackPointer);
                            sequence = irep.Sequence.AsSpan();
                            goto Next;
                        }

                        var irepProc = callInfo.Proc;
                        irep = irepProc!.Irep;
                        callInfo.ProgramCounter = irepProc.ProgramCounter;

                        context.ExtendStack(callInfo.StackPointer + (irep.RegisterVariableCount < 4 ? 4 : irep.RegisterVariableCount) + 1);
                        registers = context.Stack.AsSpan(callInfo.StackPointer);
                        sequence = irep.Sequence.AsSpan();

                        goto Next;
                        // pop on OpCode.Return
                    }
                    case OpCode.Call: // modify program counter
                    {
                        callInfo.ProgramCounter += 1; // read opcode

                        var receiver = registers[0];
                        var proc = receiver.As<RProc>();

                        // replace callinfo
                        callInfo.Scope = proc.Scope!.TargetClass;
                        callInfo.Proc = proc;

                        // setup environment for calling method
                        irep = proc.Irep;
                        sequence = irep.Sequence.AsSpan();
                        callInfo.ProgramCounter = proc.ProgramCounter;

                        if (callInfo.BlockArgumentOffset + 1 < irep.RegisterVariableCount)
                        {
                            context.ExtendStack(irep.RegisterVariableCount);
                            context.ClearStack(
                                callInfo.StackPointer + callInfo.BlockArgumentOffset + 1,
                                irep.RegisterVariableCount - callInfo.BlockArgumentOffset + 1);
                        }
                        registers = context.Stack.AsSpan(callInfo.StackPointer);
                        if (proc.Scope is REnv env)
                        {
                            callInfo.MethodId = env.MethodId;
                            registers[0] = env.Stack.Span[0];
                        }
                        goto Next;
                    }
                    case OpCode.Super:
                    {
                        Markers.Super();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        var targetClass = callInfo.Scope.TargetClass;
                        var methodId = callInfo.MethodId;
                        if (methodId == default || targetClass == null!)
                        {
                            Raise(Names.NoMethodError, "super called outside of method"u8);
                        }

                        var receiver = registers[0];
                        if (targetClass!.HasFlag(MRubyObjectFlags.ClassPrepended) ||
                            targetClass.VType == MRubyVType.Module ||
                            !KindOf(receiver, targetClass))
                        {
                            Raise(Names.TypeError, "self has wrong type to call super in this context"u8);
                        }

                        registers[bb.A] = receiver;

                        // Jump to send
                        var nextStackPointer = callInfo.StackPointer + bb.A;
                        callInfo = ref context.PushCallStack();
                        callInfo.CallerType = CallerType.InVmLoop;
                        callInfo.Scope = targetClass.Super;
                        callInfo.StackPointer = nextStackPointer;
                        callInfo.MethodId = methodId;
                        callInfo.ArgumentCount = (byte)(bb.B & 0xf);
                        callInfo.KeywordArgumentCount = (byte)((bb.B >> 4) & 0xf);
                        goto case OpCode.SendInternal;
                    }
                    case OpCode.Enter:
                    {
                        Markers.Enter();
                        bbb = OperandBBB.Read(sequence, ref callInfo.ProgramCounter);
                        var bits = (uint)bbb.A << 16 | (uint)bbb.B << 8 | bbb.C;
                        var aspec = new ArgumentSpec(bits);

                        var argc = callInfo.ArgumentCount;
                        var argv = registers.Slice(1, argc);

                        var m1 = aspec.MandatoryArguments1Count;

                        // fast pass
                        if ((bits & ~0b11111000000000000000001) == 0 && // no other arg
                            !callInfo.ArgumentPacked &&
                            callInfo.Proc?.HasFlag(MRubyObjectFlags.ProcStrict) == true)
                        {
                            // clear local (but non-argument) variables
                            var count = m1 + 2; // self + m1 + block
                            if (irep.LocalVariables.Length - count > 0)
                            {
                                context.ClearStack(
                                    callInfo.StackPointer + count,
                                    irep.LocalVariables.Length - count);
                            }
                            goto Next;
                        }

                        var o = aspec.OptionalArgumentsCount;
                        var r = aspec.TakeRestArguments ? 1 : 0;
                        var m2 = aspec.MandatoryArguments2Count;
                        // mrb_int kd = (MRB_ASPEC_KEY(a) > 0 || MRB_ASPEC_KDICT(a))? 1 : 0;
                        var argv0 = argv.IsEmpty ? MRubyValue.Nil : argv[0];

                        var mandantryTotalRequired = m1 + o + r + m2;
                        var block = registers[callInfo.BlockArgumentOffset];
                        var kdict = MRubyValue.Nil;
                        var hasAnyKeyword = aspec.KeywordArgumentsCount > 0 || aspec.TakeKeywordDict;

                        // keyword arguments
                        if (callInfo.KeywordArgumentPacked)
                        {
                            kdict = registers[callInfo.KeywordArgumentOffset];
                        }

                        if (!hasAnyKeyword)
                        {
                            if (kdict.Object is RHash { Length: > 0 })
                            {
                                switch (argc)
                                {
                                    // packed
                                    case MRubyCallInfo.CallMaxArgs:
                                        // push kdict to packed arguments
                                        registers[1].As<RArray>().Push(kdict);
                                    break;
                                    case MRubyCallInfo.CallMaxArgs - 1:
                                    {
                                        // pack arguments and kdict
                                        var packed = NewArray(registers.Slice(1, argc + 1));
                                        registers[1] = MRubyValue.From(packed);
                                        argc = callInfo.ArgumentCount = MRubyCallInfo.CallMaxArgs;
                                        break;
                                    }
                                    default:
                                        callInfo.ArgumentCount++;
                                        argc++; // include kdict in normal arguments
                                    break;
                                }
                            }
                            kdict = MRubyValue.Nil;
                            callInfo.KeywordArgumentCount = 0;
                        }
                        else if (aspec.KeywordArgumentsCount > 0 && !kdict.IsNil)
                        {
                            kdict = MRubyValue.From(kdict.As<RHash>().Dup());
                        }

                        // arguments is passed with Array
                        if (callInfo.ArgumentPacked)
                        {
                            argv = argv0.As<RArray>().AsSpan();
                            argc = (byte)argv.Length;
                        }

                        // strict argument check
                        if (callInfo.Proc?.HasFlag(MRubyObjectFlags.ProcStrict) == true)
                        {
                            if (argc < m1 + m2 || (r == 0 && argc > mandantryTotalRequired))
                            {
                                RaiseArgumentNumberError(m1 + m2);
                            }
                        }
                        // extract first argument array to arguments
                        else if (mandantryTotalRequired > 1 && argc == 1 && argv[0].Object is RArray array)
                        {
                            argc = (byte)array.Length;
                            argv = array.AsSpan();
                        }

                        // rest arguments
                        var rest = MRubyValue.Nil;
                        if (argc < mandantryTotalRequired)
                        {
                            var mlen = (int)m2;
                            if (argc < m1 + m2)
                            {
                                mlen = m1 < argc ? argc - m1 : 0;
                            }

                            if (!argv.IsEmpty && argv[0] != argv0)
                            {
                                argv[..(argc - mlen)].CopyTo(registers[1..]); // m1 + o
                            }
                            if (argc < m1)
                            {
                                registers.Slice(argc + 1, m1 - argc).Clear();
                            }

                            // copy post mandatory arguments
                            if (mlen > 0)
                            {
                                argv.Slice(argc - mlen, mlen)
                                    .CopyTo(registers[(mandantryTotalRequired - m2 + 1)..]);
                            }
                            if (mlen < m2)
                            {
                                registers.Slice(mandantryTotalRequired - m2 + mlen + 1, m2 - mlen + 1);
                            }

                            // initialize rest arguments with empty Array
                            if (r > 0)
                            {
                                rest = MRubyValue.From(NewArray(0));
                                registers[m1 + o + 1] = rest;
                            }

                            // skip initializer of passed arguments
                            if (o > 0 && argc > m1 + m2)
                            {
                                callInfo.ProgramCounter += (argc - m1 - m2) * 3;
                            }
                        }
                        else
                        {
                            var restElementLength = 0;
                            if (!argv.IsEmpty && argv0 != argv[0])
                            {
                                argv[..(m1 + o)].CopyTo(registers[1..]);
                            }
                            if (r > 0)
                            {
                                restElementLength = argc - m1 - o - m2;
                                rest = MRubyValue.From(NewArray(argv.Slice(m1 + o, restElementLength)));
                                registers[m1 + o + 1] = rest;
                            }

                            if (m2 > 0 && argc - m2 > m1)
                            {
                                argv[(m1 + o + restElementLength)..].CopyTo(registers[(m1 + o + r + 1)..]);
                            }
                            callInfo.ProgramCounter += o * 3;
                        }

                        // need to be update blk first to protect blk from GC
                        var keywordPos = mandantryTotalRequired + (hasAnyKeyword ? 1 : 0);
                        var blockPos = keywordPos + 1;
                        registers[blockPos] = block;
                        if (hasAnyKeyword)
                        {
                            if (kdict.IsNil) kdict = MRubyValue.From(NewHash(0));
                            registers[keywordPos] = kdict;
                            callInfo.MarkAsKeywordArgumentPacked();
                        }

                        // format arguments for generated code
                        callInfo.ArgumentCount = (byte)mandantryTotalRequired;
                        // clear local (but non-argument) variables
                        if (irep.LocalVariables.Length - blockPos - 1 > 0)
                        {
                            registers.Slice(blockPos + 1, irep.LocalVariables.Length - blockPos - 1).Clear();
                        }
                        goto Next;
                    }
                    case OpCode.KArg:
                    {
                        Markers.KArg();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        // mrb_value k = mrb_symbol_value(irep->syms[b]);
                        var key = MRubyValue.From(irep.Symbols[bb.B]);
                        var kargOffset = callInfo.KeywordArgumentOffset;
                        if (kargOffset < 0)
                        {
                            Raise(Names.ArgumentError, NewString($"missing keyword: {Stringify(key)}"));
                        }
                        var kdict = registers[kargOffset];
                        var value = MRubyValue.Nil;
                        if (kdict.VType != MRubyVType.Hash ||
                            !registers[kargOffset].As<RHash>().TryGetValue(key, out value))
                        {
                            Raise(Names.ArgumentError, NewString($"missing keyword: {Stringify(key)}"));
                        }

                        registers[bb.A] = value;
                        kdict.As<RHash>().Delete(key);
                        goto Next;
                    }
                    case OpCode.KeyP:
                    {
                        Markers.KeyP();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        var key = MRubyValue.From(irep.Symbols[bb.B]);
                        var kdict = registers[callInfo.KeywordArgumentOffset];
                        registers[bb.A] = MRubyValue.From(kdict.As<RHash>().TryGetValue(key, out _));
                        goto Next;
                    }
                    case OpCode.KeyEnd:
                    {
                        Markers.KeyEnd();
                        callInfo.ProgramCounter++;
                        var kargOffset = callInfo.KeywordArgumentOffset;
                        if (kargOffset >= 0 &&
                            registers[kargOffset].Object is RHash { Length: > 0 } hash)
                        {
                            var key1 = hash.Keys.First();
                            Raise(Names.ArgumentError, NewString($"unknown keyword: {Stringify(key1)}"));
                        }
                        goto Next;
                    }
                    case OpCode.Return:
                    {
                        Markers.Return();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        var returnValue = registers[a];
                        if (TryReturnJump(ref callInfo, context.CallDepth, registers[a]))
                        {
                            goto JumpAndNext;
                        }
                        return returnValue;
                    }
                    case OpCode.ReturnBlk:
                    {
                        Markers.ReturnBlk();
                        if (callInfo.Proc?.HasFlag(MRubyObjectFlags.ProcStrict) == true ||
                            callInfo.Proc?.Scope is not REnv)
                        {
                            goto case OpCode.Return;
                        }

                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        var dest = callInfo.Proc.FindReturningDestination(out var env);
                        if (dest.Scope is not REnv destEnv || destEnv.Context == context)
                        {
                            // check jump destination
                            for (var i = context.CallDepth; i >= 0; i--)
                            {
                                if (context.CallStack[i].Scope == env)
                                {
                                    var returnValue = registers[a];
                                    if (TryReturnJump(ref callInfo, i, returnValue))
                                    {
                                        goto JumpAndNext;
                                    }
                                    return returnValue;
                                }
                            }
                        }
                        // no jump destination
                        Raise(Names.LocalJumpError, "unexpected return"u8);
                        goto Next; // not reached
                    }
                    case OpCode.Break:
                    {
                        Markers.Break();
                        if (callInfo.Proc is { } x && x.HasFlag(MRubyObjectFlags.ProcStrict))
                        {
                            goto case OpCode.Return;
                        }

                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        if (callInfo.Proc is { } proc &&
                            !proc.HasFlag(MRubyObjectFlags.ProcOrphan) &&
                            proc.Scope is REnv env && env.Context == context)
                        {
                            var dest = proc.Upper;
                            for (var i = context.CallDepth; i > 0; i--)
                            {
                                if (context.CallStack[i - 1].Proc == dest)
                                {
                                    var returnValue = registers[a];
                                    if (TryReturnJump(ref callInfo, i, returnValue))
                                    {
                                        goto JumpAndNext;
                                    }
                                    return returnValue;
                                }
                            }
                        }
                        Raise(Names.LocalJumpError, "break from proc-closure"u8);
                        goto Next; // not reached
                    }
                    case OpCode.BlkPush:
                    {
                        Markers.BlkPush();
                        bs = OperandBS.Read(sequence, ref callInfo.ProgramCounter);
                        var b = bs.B;
                        var m1 = (b >> 11) & 0x3f;
                        var r = (b >> 10) & 0x1;
                        var m2 = (b >> 5) & 0x1f;
                        var kd = (b >> 4) & 0x1;
                        var lv = (b >> 0) & 0xf;
                        var offset = m1 + r + m2 + kd;

                        ReadOnlySpan<MRubyValue> stack;
                        if (lv == 0)
                        {
                            stack = registers[1..];
                        }
                        else
                        {
                            var env = callInfo.Proc?.FindUpperEnvTo(lv - 1);
                            if (env == null ||
                                (!env.OnStack && env.MethodId == default) ||
                                env.Stack.Length <= offset + 1)
                            {
                                Raise(Names.LocalJumpError, "unexpected yield"u8);
                            }
                            stack = env!.Stack.Span[1..];
                        }

                        var block = stack[offset];
                        if (block.IsNil)
                        {
                            Raise(Names.LocalJumpError, "unexpected yield"u8);
                        }
                        registers[bs.A] = block;
                        goto Next;
                    }
                    case OpCode.Add:
                        Markers.Add();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        registerA = ref registers[a];
                        var rhs = Unsafe.Add(ref registerA, 1);
                    {
                        switch (registerA.VType, rhs.VType)
                        {
                            case (MRubyVType.Integer, MRubyVType.Integer):
                                // TODO: overflow handling
                                registerA = NewInteger(registerA.IntegerValue + rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Integer, MRubyVType.Float):
                                registerA = MRubyValue.From(registerA.IntegerValue + rhs.FloatValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Integer):
                                registerA = MRubyValue.From(registerA.FloatValue + rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Float):
                                registerA = MRubyValue.From(registerA.FloatValue + rhs.FloatValue);
                                goto Next;
                            case (MRubyVType.String, MRubyVType.String):
                                registerA = MRubyValue.From(registerA.As<RString>() + rhs.As<RString>());
                                goto Next;
                        }
                        // Jump to send :+
                        var nextStackPointer = callInfo.StackPointer + a;
                        callInfo = ref context.PushCallStack();
                        callInfo.CallerType = CallerType.InVmLoop;
                        callInfo.StackPointer = nextStackPointer;
                        callInfo.MethodId = Names.OpAdd;
                        callInfo.ArgumentCount = 1;
                        callInfo.KeywordArgumentCount = 0;
                        goto case OpCode.SendInternal;
                    }
                    case OpCode.AddI:
                    case OpCode.SubI:
                        Markers.AddI();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        registerA = ref registers[bb.A];
                    {
                        var rV = opcode == OpCode.AddI ? bb.B : -bb.B;
                        switch (registerA.VType)
                        {
                            case MRubyVType.Integer:
                                registerA = NewInteger(registerA.IntegerValue + rV);
                                goto Next;
                            case MRubyVType.Float:
                                registerA = MRubyValue.From(registerA.FloatValue + rV);
                                goto Next;
                        }

                        // Jump to send :+ or :-
                        Unsafe.Add(ref registerA, 1) = MRubyValue.From(rV);
                        var nextStackPointer = callInfo.StackPointer + bb.A;
                        callInfo = ref context.PushCallStack();
                        callInfo.CallerType = CallerType.InVmLoop;
                        callInfo.StackPointer = nextStackPointer;
                        callInfo.MethodId = opcode == OpCode.AddI ? Names.OpAdd : Names.OpSub;
                        callInfo.ArgumentCount = 1;
                        callInfo.KeywordArgumentCount = 0;
                        goto case OpCode.SendInternal;
                    }
                    case OpCode.Sub:
                    {
                        Markers.Sub();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        registerA = ref registers[a];
                        rhs = Unsafe.Add(ref registerA, 1);
                        switch (registerA.VType, rhs.VType)
                        {
                            case (MRubyVType.Integer, MRubyVType.Integer):
                                // TODO: overflow handling
                                registerA = NewInteger(registerA.IntegerValue - rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Integer, MRubyVType.Float):
                                registerA = MRubyValue.From(registerA.IntegerValue - rhs.FloatValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Integer):
                                registerA = MRubyValue.From(registerA.FloatValue - rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Float):
                                registerA = MRubyValue.From(registerA.FloatValue - rhs.FloatValue);
                                goto Next;
                        }

                        // Jump to send :-
                        var nextStackPointer = callInfo.StackPointer + a;
                        callInfo = ref context.PushCallStack();
                        callInfo.CallerType = CallerType.InVmLoop;
                        callInfo.StackPointer = nextStackPointer;
                        callInfo.MethodId = Names.OpSub;
                        callInfo.ArgumentCount = 1;
                        callInfo.KeywordArgumentCount = 0;
                        goto case OpCode.SendInternal;
                    }
                    case OpCode.Mul:
                    {
                        Markers.Mul();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        registerA = ref registers[a];
                        rhs = Unsafe.Add(ref registerA, 1);
                        switch (registerA.VType, rhs.VType)
                        {
                            case (MRubyVType.Integer, MRubyVType.Integer):
                                // TODO: overflow handling
                                registerA = NewInteger(registerA.IntegerValue * rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Integer, MRubyVType.Float):
                                registerA = MRubyValue.From(registerA.IntegerValue * rhs.FloatValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Integer):
                                registerA = MRubyValue.From(registerA.FloatValue * rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Float):
                                registerA = MRubyValue.From(registerA.FloatValue * rhs.FloatValue);
                                goto Next;
                        }
                        // Jump to send :*
                        var nextStackPointer = callInfo.StackPointer + a;
                        callInfo = ref context.PushCallStack();
                        callInfo.CallerType = CallerType.InVmLoop;
                        callInfo.StackPointer = nextStackPointer;
                        callInfo.MethodId = Names.OpMul;
                        callInfo.ArgumentCount = 1;
                        callInfo.KeywordArgumentCount = 0;
                        goto case OpCode.SendInternal;
                    }
                    case OpCode.Div:
                    {
                        Markers.Div();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        registerA = ref registers[a];
                        rhs = Unsafe.Add(ref registerA, 1);
                        switch (registerA.VType, rhs.VType)
                        {
                            case (MRubyVType.Integer, MRubyVType.Integer):
                                // TODO: overflow handling
                                registerA = NewInteger(registerA.IntegerValue / rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Integer, MRubyVType.Float):
                                registerA = MRubyValue.From(registerA.IntegerValue / rhs.FloatValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Integer):
                                registerA = MRubyValue.From(registerA.FloatValue / rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Float):
                                registerA = MRubyValue.From(registerA.FloatValue / rhs.FloatValue);
                                goto Next;
                        }
                        // Jump to send :/
                        var nextStackPointer = callInfo.StackPointer + a;
                        callInfo = ref context.PushCallStack();
                        callInfo.CallerType = CallerType.InVmLoop;
                        callInfo.StackPointer = nextStackPointer;
                        callInfo.MethodId = Names.OpDiv;
                        callInfo.ArgumentCount = 1;
                        callInfo.KeywordArgumentCount = 0;
                        goto case OpCode.SendInternal;
                    }
                    case OpCode.EQ:
                    {
                        Markers.EQ();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        registerA = ref registers[a];
                        rhs = Unsafe.Add(ref registerA, 1);
                        if (registerA.Equals(rhs))
                        {
                            registerA = MRubyValue.True;
                            goto Next;
                        }
                        if (registerA.IsSymbol)
                        {
                            registerA = MRubyValue.False;
                            goto Next;
                        }
                        switch (registerA.VType, rhs.VType)
                        {
                            case (MRubyVType.Integer, MRubyVType.Integer):
                                registerA = MRubyValue.From(registerA.IntegerValue == rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Integer, MRubyVType.Float):
                                registerA = MRubyValue.From(registerA.IntegerValue == (long)rhs.FloatValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Integer):
                                registerA = MRubyValue.From((long)registerA.FloatValue == rhs.IntegerValue);
                                goto Next;
                            // ReSharper disable once CompareOfFloatsByEqualityOperator
                            case (MRubyVType.Float, MRubyVType.Float):
                                registerA = MRubyValue.From(registerA.FloatValue == rhs.FloatValue);
                                goto Next;
                        }

                        // Jump to send :==
                        var nextStackPointer = callInfo.StackPointer + a;
                        callInfo = ref context.PushCallStack();
                        callInfo.CallerType = CallerType.InVmLoop;
                        callInfo.StackPointer = nextStackPointer;
                        callInfo.MethodId = Names.OpEq;
                        callInfo.ArgumentCount = 1;
                        callInfo.KeywordArgumentCount = 0;
                        goto case OpCode.SendInternal;
                    }
                    case OpCode.LT:
                    {
                        Markers.LT();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        registerA = ref registers[a];
                        rhs = Unsafe.Add(ref registerA, 1);
                        switch (registerA.VType, rhs.VType)
                        {
                            case (MRubyVType.Integer, MRubyVType.Integer):
                                registerA = MRubyValue.From(registerA.IntegerValue < rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Integer, MRubyVType.Float):
                                registerA = MRubyValue.From(registerA.IntegerValue < (long)rhs.FloatValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Integer):
                                registerA = MRubyValue.From((long)registerA.FloatValue < rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Float):
                                registerA = MRubyValue.From(registerA.FloatValue < rhs.FloatValue);
                                goto Next;
                        }

                        // Jump to send :==
                        var nextStackPointer = callInfo.StackPointer + a;
                        callInfo = ref context.PushCallStack();
                        callInfo.CallerType = CallerType.InVmLoop;
                        callInfo.StackPointer = nextStackPointer;
                        callInfo.MethodId = Names.OpLt;
                        callInfo.ArgumentCount = 1;
                        callInfo.KeywordArgumentCount = 0;
                        goto case OpCode.SendInternal;
                    }
                    case OpCode.LE:
                    {
                        Markers.LE();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        registerA = ref registers[a];
                        rhs = Unsafe.Add(ref registerA, 1);

                        switch (registerA.VType, rhs.VType)
                        {
                            case (MRubyVType.Integer, MRubyVType.Integer):
                                registerA = MRubyValue.From(registerA.IntegerValue <= rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Integer, MRubyVType.Float):
                                registerA = MRubyValue.From(registerA.IntegerValue <= (long)rhs.FloatValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Integer):
                                registerA = MRubyValue.From((long)registerA.FloatValue <= rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Float):
                                registerA = MRubyValue.From(registerA.FloatValue <= rhs.FloatValue);
                                goto Next;
                        }

                        // Jump to send :<=
                        var nextStackPointer = callInfo.StackPointer + a;
                        callInfo = ref context.PushCallStack();
                        callInfo.CallerType = CallerType.InVmLoop;
                        callInfo.StackPointer = nextStackPointer;
                        callInfo.MethodId = Names.OpLe;
                        callInfo.ArgumentCount = 1;
                        callInfo.KeywordArgumentCount = 0;
                        goto case OpCode.SendInternal;
                    }
                    case OpCode.GT:
                    {
                         Markers.GT();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        registerA = ref registers[a];
                        rhs = Unsafe.Add(ref registerA, 1);

                        switch (registerA.VType, rhs.VType)
                        {
                            case (MRubyVType.Integer, MRubyVType.Integer):
                                registerA = MRubyValue.From(registerA.IntegerValue > rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Integer, MRubyVType.Float):
                                registerA = MRubyValue.From(registerA.IntegerValue > (long)rhs.FloatValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Integer):
                                registerA = MRubyValue.From((long)registerA.FloatValue > rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Float):
                                registerA = MRubyValue.From(registerA.FloatValue > rhs.FloatValue);
                                goto Next;
                        }
                        // Jump to send :>
                        var nextStackPointer = callInfo.StackPointer + a;
                        callInfo = ref context.PushCallStack();
                        callInfo.CallerType = CallerType.InVmLoop;
                        callInfo.StackPointer = nextStackPointer;
                        callInfo.MethodId = Names.OpGt;
                        callInfo.ArgumentCount = 1;
                        callInfo.KeywordArgumentCount = 0;
                        goto case OpCode.SendInternal;
                    }
                    case OpCode.GE:
                    {
                        Markers.GE();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        registerA = ref registers[a];
                        rhs = Unsafe.Add(ref registerA, 1);
                        switch (registerA.VType, rhs.VType)
                        {
                            case (MRubyVType.Integer, MRubyVType.Integer):
                                registerA = MRubyValue.From(registerA.IntegerValue >= rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Integer, MRubyVType.Float):
                                registerA = MRubyValue.From(registerA.IntegerValue >= (long)rhs.FloatValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Integer):
                                registerA = MRubyValue.From((long)registerA.FloatValue >= rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Float):
                                registerA = MRubyValue.From(registerA.FloatValue >= rhs.FloatValue);
                                goto Next;
                        }
                        // Jump to send :>=
                        var nextStackPointer = callInfo.StackPointer + a;
                        callInfo = ref context.PushCallStack();
                        callInfo.CallerType = CallerType.InVmLoop;
                        callInfo.StackPointer = nextStackPointer;
                        callInfo.MethodId = Names.OpGe;
                        callInfo.ArgumentCount = 1;
                        callInfo.KeywordArgumentCount = 0;
                        goto case OpCode.SendInternal;
                    }
                    case OpCode.Array:
                    {
                        Markers.Array();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        var values = registers.Slice(bb.A, bb.B);
                        registers[bb.A] = MRubyValue.From(NewArray(values));
                        goto Next;
                    }
                    case OpCode.Array2:
                    {
                        Markers.Array2();
                        bbb = OperandBBB.Read(sequence, ref callInfo.ProgramCounter);
                        var values = registers.Slice(bbb.B, bbb.C);
                        registers[bbb.A] = MRubyValue.From(NewArray(values));
                        goto Next;
                    }
                    case OpCode.AryCat:
                    {
                        Markers.AryCat();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        registerA = ref registers[a];
                        var splat = SplatArray(Unsafe.Add(ref registerA, 1));
                        if (registerA.IsNil)
                        {
                            registerA = splat;
                        }
                        else
                        {
                            EnsureValueType(registerA, MRubyVType.Array);
                            var array = registerA.As<RArray>();
                            array.Concat(splat.As<RArray>());
                        }
                        goto Next;
                    }
                    case OpCode.ARef:
                    {
                        Markers.ARef();
                        bbb = OperandBBB.Read(sequence, ref callInfo.ProgramCounter);
                        registerA = ref registers[bbb.A];
                        var v = registers[bbb.B];
                        if (v.VType == MRubyVType.Array)
                        {
                            registerA = v.As<RArray>()[bbb.C];
                        }
                        else
                        {
                            if (bbb.C == 0)
                            {
                                registerA = v;
                            }
                            else
                            {
                                registerA = MRubyValue.Nil;
                            }
                        }
                        goto Next;
                    }
                    case OpCode.ASet:
                    {
                        Markers.ASet();
                        bbb = OperandBBB.Read(sequence, ref callInfo.ProgramCounter);
                        var array = registers[bbb.B].As<RArray>();
                        array[bbb.C] = registers[bbb.A];
                        goto Next;
                    }
                    case OpCode.APost:
                    {
                        Markers.APost();
                        bbb = OperandBBB.Read(sequence, ref callInfo.ProgramCounter);
                        registerA = ref registers[bbb.A];
                        if (registerA.Object is not RArray array)
                        {
                            array = NewArray(registerA);
                        }
                        int pre = bbb.B;
                        int post = bbb.C;
                        if (array.Length > pre + post)
                        {
                            var slice = array.AsSpan().Slice(bbb.B, array.Length - pre - post);
                            registerA = MRubyValue.From(NewArray(slice));
                            registerA = ref Unsafe.Add(ref registerA, 1);
                            while (post-- > 0)
                            {
                                registerA = array[array.Length - post - 1];
                                registerA = ref Unsafe.Add(ref registerA, 1);
                            }
                        }
                        else
                        {
                            registerA = MRubyValue.From(NewArray(0));
                            registerA = ref Unsafe.Add(ref registerA, 1);
                            int i;
                            for (i = 0; i + pre < array.Length; i++)
                            {
                                Unsafe.Add(ref registerA, i) = array[pre + i];
                            }
                            while (i < post)
                            {
                                Unsafe.Add(ref registerA, i) = MRubyValue.Nil;
                                i++;
                            }
                        }
                        goto Next;
                    }
                    case OpCode.AryPush:
                    {
                        Markers.AryPush();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        registerA = ref registers[bb.A];
                        EnsureNotFrozen(registerA);

                        var array = registerA.As<RArray>();
                        array.PushRange(registers.Slice(bb.A+1, bb.B));
                        goto Next;
                    }
                    case OpCode.ArySplat:
                        Markers.ArySplat();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        registerA = ref registers[a];
                        registerA = SplatArray(registerA);
                        goto Next;
                    case OpCode.Intern:
                        Markers.Intern();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        registerA = ref registers[a];
                        registerA = MRubyValue.From(Intern(registerA.As<RString>()));
                        goto Next;
                    case OpCode.Symbol:
                    {
                        Markers.Symbol();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        //var name = irep.PoolValues[bb.B].As<RString>();
                        registers[bb.A] = MRubyValue.From(Intern(irep.PoolValues[bb.B].As<RString>()));
                        goto Next;
                    }
                    case OpCode.String:
                    {
                        Markers.String();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        var str = irep.PoolValues[bb.B].As<RString>();
                        registers[bb.A] = MRubyValue.From(str.Dup());
                        goto Next;
                    }
                    case OpCode.StrCat:
                        Markers.StrCat();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        registerA = ref registers[a];
                        registerA.As<RString>().Concat(Stringify(Unsafe.Add(ref registerA, 1)));
                        goto Next;
                    case OpCode.Hash:
                    {
                        Markers.Hash();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        registerA = ref registers[bb.A];
                        var hash = NewHash(bb.B);
                        var lastIndex = bb.B * 2;
                        for (var i = 0; i < lastIndex; i += 2)
                        {
                            hash.Add(Unsafe.Add(ref registerA, i), Unsafe.Add(ref registerA, i + 1));
                        }

                        registerA = MRubyValue.From(hash);
                        goto Next;
                    }
                    case OpCode.HashAdd:
                    {
                        Markers.HashAdd();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        registerA = ref registers[bb.A];
                        var hashValue = registerA;
                        var lastIndex = bb.B * 2 + 1;

                        EnsureValueType(hashValue, MRubyVType.Hash);
                        var hash = hashValue.As<RHash>();
                        for (var i = 1; i < lastIndex; i += 2)
                        {
                            hash.Add(Unsafe.Add(ref registerA, i), Unsafe.Add(ref registerA, i + 1));
                        }
                        goto Next;
                    }
                    case OpCode.HashCat:
                        Markers.HashCat();
                        a = sequence[++callInfo.ProgramCounter];
                        ++callInfo.ProgramCounter;
                        registerA = ref registers[a];
                        EnsureNotFrozen(registerA);
                        registerA.As<RHash>().Merge(Unsafe.Add(ref registerA, 1).As<RHash>());
                        goto Next;
                    case OpCode.Lambda:
                    case OpCode.Block:
                    case OpCode.Method:
                    {
                        Markers.Lambda();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        var proc = NewClosure(irep.Children[bb.B]);
                        if (opcode == OpCode.Lambda)
                            proc.SetFlag(MRubyObjectFlags.ProcStrict);
                        else if (opcode == OpCode.Method)
                        {
                            proc.SetFlag(MRubyObjectFlags.ProcStrict);
                            proc.SetFlag(MRubyObjectFlags.ProcScope);
                        }
                        registers[bb.A] = MRubyValue.From(proc);
                        goto Next;
                    }
                    case OpCode.RangeInc:
                    case OpCode.RangeExc:
                        Markers.RangeInc();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        registerA = ref registers[a];
                    {
                        var begin = registerA;
                        var end = Unsafe.Add(ref registerA, 1);
                        var range = new RRange(begin, end, opcode == OpCode.RangeExc, RangeClass);
                        registers[a] = MRubyValue.From(range);
                        goto Next;
                    }
                    case OpCode.OClass:
                        Markers.OClass();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        registers[a] = MRubyValue.From(ObjectClass);
                        goto Next;
                    case OpCode.Class:
                    {
                        Markers.Class();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        var id = irep.Symbols[bb.B];
                        var outer = registers[bb.A];
                        var super = registers[bb.A + 1];

                        var outerClass = outer.IsNil
                            ? callInfo.Proc?.Scope?.TargetClass ?? ObjectClass
                            : outer.As<RClass>();

                        // mrb_vm_define_class
                        RClass? superClass = null;
                        RClass definedClass;
                        if (!super.IsNil)
                        {
                            if (super.Object is RClass sc)
                            {
                                superClass = sc;
                            }
                            else
                            {
                                Raise(Names.TypeError, NewString($"superclass must be a Class ({Stringify(super)} given)"));
                            }
                        }

                        if (ConstDefinedAt(id, outerClass))
                        {
                            var old = GetConst(id, outerClass);
                            if (!old.IsClass)
                            {
                                Raise(Names.TypeError, NewString($"{StringifyAny(old)} is not a class)"));
                            }

                            definedClass = old.As<RClass>();
                            if (superClass != null)
                            {
                                // check super class
                                if (definedClass.Super.GetRealClass() != superClass)
                                {
                                    Raise(Names.TypeError, NewString($"superclass mismatch for {Stringify(old)}"));
                                }
                            }
                        }
                        else
                        {
                            superClass ??= ObjectClass;
                            definedClass = DefineClass(id, superClass, superClass.InstanceVType, outerClass);
                            ClassInheritedHook(superClass.GetRealClass(), definedClass);
                        }
                        registers[bb.A] = MRubyValue.From(definedClass);
                        goto Next;
                    }
                    case OpCode.Module:
                    {
                        Markers.Module();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        registerA = ref registers[bb.A];
                        var id = irep.Symbols[bb.B];
                        var outerClass = registerA.IsNil
                            ? callInfo.Proc?.Scope?.TargetClass ?? ObjectClass
                            : registerA.As<RClass>();

                        RClass definedModule;
                        if (ConstDefinedAt(id, outerClass))
                        {
                            var old = GetConst(id, outerClass);
                            if (old.VType != MRubyVType.Module)
                            {
                                Raise(Names.TypeError, NewString($"{StringifyAny(old)} is not a module"));
                            }
                            definedModule = old.As<RClass>();
                        }
                        else
                        {
                            definedModule = DefineModule(id, outerClass);
                        }
                        registerA = MRubyValue.From(definedModule);
                        goto Next;
                    }
                    case OpCode.Exec:
                    {
                        Markers.Exec();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        var receiver = registers[bb.A];
                        var targetIrep = irep.Children[bb.B];

                        // prepare closure
                        var proc = NewProc(targetIrep, receiver.As<RClass>());
                        proc.SetFlag(MRubyObjectFlags.ProcScope);

                        // prepare callstack
                        ref var nextCallInfo = ref context.PushCallStack();
                        nextCallInfo.StackPointer = callInfo.StackPointer + bb.A;
                        nextCallInfo.CallerType = CallerType.InVmLoop;
                        nextCallInfo.Scope = receiver.As<RClass>();
                        nextCallInfo.Proc = proc;
                        nextCallInfo.MethodId = default;
                        nextCallInfo.ArgumentCount = 0;
                        nextCallInfo.KeywordArgumentCount = 0;
                        nextCallInfo.ProgramCounter = 0;

                        // modify local variable and jump
                        callInfo = ref nextCallInfo;

                        irep = callInfo.Proc!.Irep;
                        sequence = irep.Sequence.AsSpan();

                        context.ExtendStack(callInfo.StackPointer + irep.RegisterVariableCount + 1);
                        context.ClearStack(callInfo.StackPointer + 1, irep.RegisterVariableCount - 1);

                        registers = context.Stack.AsSpan(nextCallInfo.StackPointer);
                        goto Next;
                    }
                    case OpCode.Def:
                    {
                        Markers.Def();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        var target = registers[bb.A].As<RClass>();
                        var proc = registers[bb.A + 1].As<RProc>();
                        var methodId = irep.Symbols[bb.B];

                        DefineMethod(target, methodId, new MRubyMethod(proc));
                        MethodAddedHook(target, methodId);
                        registers[bb.A] = MRubyValue.From(methodId);
                        goto Next;
                    }
                    case OpCode.Alias:
                    {
                        Markers.Alias();
                        bb = OperandBB.Read(sequence, ref callInfo.ProgramCounter);
                        var c = callInfo.Scope.TargetClass;
                        var newMethodId = irep.Symbols[bb.A];
                        var oldMethodId = irep.Symbols[bb.B];
                        AliasMethod(c, newMethodId, oldMethodId);
                        MethodAddedHook(c, newMethodId);
                        goto Next;
                    }
                    case OpCode.Undef:
                    {
                        Markers.Undef();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        var c = callInfo.Scope.TargetClass;
                        var methodId = irep.Symbols[a];
                        UndefMethod(c, methodId);
                        goto Next;
                    }
                    case OpCode.SClass:
                    {
                        Markers.SClass();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        registerA = ref registers[a];
                        var result = SingletonClassOf(registerA);
                        registerA = result != null ? MRubyValue.From(result) : MRubyValue.Nil;
                        goto Next;
                    }
                    case OpCode.TClass:
                    {
                        Markers.TClass();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        registers[a] = MRubyValue.From(callInfo.Scope.TargetClass);
                        goto Next;
                    }
                    case OpCode.Err:
                    {
                        Markers.Err();
                        a = ReadOperandB(sequence, ref callInfo.ProgramCounter);
                        var message = irep.PoolValues[a];
                        Raise(Names.LocalJumpError, message.As<RString>());
                        goto Next;
                    }
                    case OpCode.Stop:
                    {
                        Markers.Stop();
                        var returnValue = Exception switch
                        {
                            MRubyRaiseException x => MRubyValue.From(x.ExceptionObject),
                            MRubyBreakException x => MRubyValue.From(x.BreakObject),
                            _ => MRubyValue.Nil
                        };
                        if (TryUnwindEnsureJump(ref callInfo, context.CallDepth, BreakTag.Stop, returnValue))
                        {
                            goto JumpAndNext;
                        }
                        if (Exception != null) throw Exception;
                        return registers[irep.LocalVariables.Length];
                    }
                    default:
                        throw new NotSupportedException($"Unknown opcode {opcode}");
                }

                Next: continue;

                JumpAndNext:
                callInfo = ref context.CurrentCallInfo;
                irep = callInfo.Proc!.Irep;
                registers = context.Stack.AsSpan(callInfo.StackPointer);
                sequence = irep.Sequence.AsSpan();
            }
            catch (MRubyRaiseException ex)
            {
                Exception = ex;
                if (TryRaiseJump(ref callInfo))
                {
                    callInfo = ref context.CurrentCallInfo;
                    irep = callInfo.Proc!.Irep;
                    registers = context.Stack.AsSpan(callInfo.StackPointer);
                    sequence = irep.Sequence.AsSpan();
                }
                else
                {
                    throw;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static byte ReadOperandB(ReadOnlySpan<byte> sequence, ref int pc)
    {
        pc += 2;
        var result = Unsafe.Add(ref MemoryMarshal.GetReference(sequence), pc - 1);
        return result;
    }

    /// I don't know why, but introducing this method makes the code faster.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static short ReadOperandS(ReadOnlySpan<byte> sequence, ref int pc)
    {
        return OperandS.Read(sequence, ref pc).A;
    }

    bool TryReturnJump(ref MRubyCallInfo callInfo, int returnDepth, MRubyValue returnValue)
    {
        while (true)
        {
            if (TryUnwindEnsureJump(ref callInfo, returnDepth, BreakTag.Break, returnValue))
            {
                return true;
            }

            if (context.CallDepth == returnDepth)
            {
                break;
            }

            var callerType = callInfo.CallerType;
            context.PopCallStack();
            callInfo = ref context.CurrentCallInfo;
            if (callerType != CallerType.InVmLoop)
            {
                Exception = new MRubyBreakException(this, new RBreak
                {
                    BreakIndex = returnDepth,
                    Tag = BreakTag.Break,
                    Value = returnValue
                });
                throw Exception;
            }
        }
        Exception = null; // Clear break object

        // root
        if (context.CallDepth == 0)
        {
            if (context == contextRoot)
            {
                // toplevel return
                return false;
            }

            // TODO: Fiber terminate
        }

        // TODO: Check Fiber switched

        var returnOffset = callInfo.StackPointer;
        context.PopCallStack();
        if (callInfo.CallerType is CallerType.VmExecuted or CallerType.MethodCalled)
        {
            return false;
        }

        context.Stack[returnOffset] = returnValue;
        return true;
    }

    bool TryUnwindEnsureJump(ref MRubyCallInfo callInfo, int returnDepth, BreakTag tag, MRubyValue value)
    {
        if (callInfo.Proc is { Irep: { CatchHandlers.Length: > 0 } irep } &&
            irep.TryFindCatchHandler(callInfo.ProgramCounter, CatchHandlerType.Ensure, out var catchHandler))
        {
            PrepareTaggedBreak(tag, returnDepth, value);
            callInfo.ProgramCounter = (int)catchHandler.Target;
            return true;
        }
        return false;
    }

    bool TryRaiseJump(ref MRubyCallInfo callInfo)
    {
        while (true)
        {
            if (callInfo.Proc is { Irep: { CatchHandlers.Length: > 0 } irep } &&
                irep.TryFindCatchHandler(callInfo.ProgramCounter, CatchHandlerType.All, out var catchHandler))
            {
                callInfo.ProgramCounter = (int)catchHandler.Target;
                return true;
            }

            if (context.CallDepth > 0)
            {
                var callerType = callInfo.CallerType;
                context.PopCallStack();
                callInfo = ref context.CurrentCallInfo;
                if (callerType == CallerType.VmExecuted)
                {
                    return false;
                }
            }
            else if (context == contextRoot)
            {
                // top-level
                return false;
            }
            else
            {
                // TODO: Fiber terminate
                throw new NotSupportedException();
            }
        }
    }

    void PrepareTaggedBreak(BreakTag tag, int callDepth, MRubyValue returnValue)
    {
        if (Exception is MRubyBreakException ex)
        {
            ex.BreakObject.Tag = tag;
        }
        else
        {
            Exception = new MRubyBreakException(this, new RBreak
            {
                BreakIndex = callDepth,
                Tag = tag,
                Value = returnValue
            });
        }
    }

    MRubyMethod PrepareMethodMissing(
        ref MRubyCallInfo callInfo,
        MRubyValue receiver,
        Symbol methodId,
        Action<MRubyState, MRubyValue, Symbol>? raise = null)
    {
        var receiverClass = ClassOf(receiver);
        var args = context.GetRestArg(ref callInfo, 0);
        if (!TryFindMethod(receiverClass, Names.MethodMissing, out var method, out _) ||
            method == BasicObjectMembers.MethodMissing)
        {
            _Raise(args);
        }

        // call :method_missing

        if (!TryFindMethod(callInfo.Scope.TargetClass, Names.MethodMissing, out var methodMissing, out _))
        {
            _Raise(args);
        }

        context.ExtendStack(callInfo.StackPointer + 5);
        var registers = context.Stack.AsSpan(callInfo.StackPointer);

        registers[1] = MRubyValue.From(NewArray(args));
        if (callInfo.KeywordArgumentCount == 0)
        {
            registers[2] = args[callInfo.BlockArgumentOffset];
        }
        else if (callInfo.KeywordArgumentPacked)
        {
            registers[2] = args[callInfo.ArgumentCount];
            registers[3] = args[callInfo.BlockArgumentOffset];
        }
        else
        {
            var hash = NewHash(callInfo.KeywordArgumentCount);
            foreach (var (key, value) in context.GetKeywordArgs(ref callInfo))
            {
                hash[MRubyValue.From(key)] = value;
            }
            registers[2] = MRubyValue.From(hash);
            registers[3] = args[callInfo.BlockArgumentOffset];
        }

        callInfo.MarkAsArgumentPacked();
        callInfo.MarkAsKeywordArgumentPacked();
        callInfo.MethodId = Names.MethodMissing;
        if (methodId != Names.MethodMissing)
        {
            callInfo.Scope = receiverClass;
        }

        return methodMissing;

        void _Raise(ReadOnlySpan<MRubyValue> args)
        {
            if (raise != null)
            {
                raise(this, receiver, methodId);
            }
            else
            {
                RaiseMethodMissing(methodId, receiver, MRubyValue.From(NewArray(args)));
            }
        }
    }
}