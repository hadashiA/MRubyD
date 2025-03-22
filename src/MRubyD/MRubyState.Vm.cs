using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using MRubyD.Internals;
using MRubyD.StdLib;

namespace MRubyD;

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

            try
            {
                return method.Invoke(this, self);
            }
            finally
            {
                context.PopCallStack();
            }
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
        ref var callInfo = ref context.CurrentCallInfo;

        var methodId = GetArgAsSymbol(0);
        if (callInfo.CallerType != CallerType.VmExecuted)
        {
            var block = GetBlockArg();
            var args = GetRestArg(1);
            var kargs = GetKeywordArgs();
            return Send(self, methodId, args, kargs, block.IsNil ? null : block.As<RProc>());
        }

        throw new NotSupportedException();
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

        var sequence = irep.Sequence.AsSpan(pc);

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
                    {
                        callInfo.ProgramCounter++;
                        goto Next;
                    }
                    case OpCode.Move:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        registers[a] = registers[b];
                        goto Next;
                    }
                    case OpCode.LoadL:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        registers[a] = irep.PoolValues[b];
                        goto Next;
                    }
                    case OpCode.LoadI8:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        registers[a] = MRubyValue.From(b);
                        goto Next;
                    }
                    case OpCode.LoadINeg:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        registers[a] = MRubyValue.From(-b);
                        goto Next;
                    }
                    case OpCode.LoadI__1:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        registers[a] = MRubyValue.From(-1);
                        goto Next;
                    }
                    case OpCode.LoadI_0:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        registers[a] = MRubyValue.From(0);
                        goto Next;
                    }
                    case OpCode.LoadI_1:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        registers[a] = MRubyValue.From(1);
                        goto Next;
                    }
                    case OpCode.LoadI_2:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        registers[a] = MRubyValue.From(2);
                        goto Next;
                    }
                    case OpCode.LoadI_3:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        registers[a] = MRubyValue.From(3);
                        goto Next;
                    }
                    case OpCode.LoadI_4:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        registers[a] = MRubyValue.From(4);
                        goto Next;
                    }
                    case OpCode.LoadI_5:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        registers[a] = MRubyValue.From(5);
                        goto Next;
                    }
                    case OpCode.LoadI_6:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        registers[a] = MRubyValue.From(6);
                        goto Next;
                    }
                    case OpCode.LoadI_7:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        registers[a] = MRubyValue.From(7);
                        goto Next;
                    }
                    case OpCode.LoadI16:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out short b);
                        registers[a] = MRubyValue.From(b);
                        goto Next;
                    }
                    case OpCode.LoadI32:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out int b);
                        registers[a] = MRubyValue.From(b);
                        goto Next;
                    }
                    case OpCode.LoadSym:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        registers[a] = MRubyValue.From(irep.Symbols[b]);
                        goto Next;
                    }
                    case OpCode.LoadNil:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        registers[a] = MRubyValue.Nil;
                        goto Next;
                    }
                    case OpCode.LoadSelf:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        registers[a] = registers[0];
                        goto Next;
                    }
                    case OpCode.LoadT:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        registers[a] = MRubyValue.True;
                        goto Next;
                    }
                    case OpCode.LoadF:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        registers[a] = MRubyValue.False;
                        goto Next;
                    }
                    case OpCode.GetGV:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        registers[a] = globalVariables.Get(irep.Symbols[b]);
                        goto Next;
                    }
                    case OpCode.SetGV:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        globalVariables.Set(irep.Symbols[b], registers[a]);
                        goto Next;
                    }
                    case OpCode.GetSV:
                    case OpCode.SetSV:
                    {
                        callInfo.ProgramCounter += 3;
                        goto Next;
                    }
                    case OpCode.GetIV:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        registers[a] = registers[0].As<RObject>().InstanceVariables.Get(irep.Symbols[b]);
                        goto Next;
                    }
                    case OpCode.SetIV:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        registers[0].As<RObject>().InstanceVariables.Set(irep.Symbols[b], registers[a]);
                        goto Next;
                    }
                    case OpCode.GetCV:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        registers[a] = GetClassVariable(irep.Symbols[b]);
                        goto Next;
                    }
                    case OpCode.SetCV:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        var name = irep.Symbols[b];
                        var value = registers[a];
                        var p = callInfo.Proc;
                        RClass? c;
                        while (true)
                        {
                            if (p == null)
                            {
                                throw new InvalidOperationException();
                            }
                            c = p.Scope switch
                            {
                                REnv env => env.TargetClass,
                                RClass scopeClass => scopeClass,
                                _ => null
                            };
                            if (c != null && c.VType != MRubyVType.SClass)
                            {
                                break;
                            }
                            p = p!.Upper;
                        }
                        SetConst(name, c, value);
                        goto Next;
                    }
                    case OpCode.GetConst:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        var id = irep.Symbols[b];
                        var c = callInfo.Proc?.Scope?.TargetClass ?? ObjectClass;
                        if (c.InstanceVariables.TryGet(id, out var value))
                        {
                            registers[a] = value;
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
                                registers[a] = value;
                                goto Next;
                            }
                            proc = proc.Upper;
                        }
                        registers[a] = GetConst(id, c);
                        goto Next;
                    }
                    case OpCode.SetConst:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        var value = registers[a];
                        var id = irep.Symbols[b];

                        var c = callInfo.Proc?.Scope?.TargetClass ?? ObjectClass;
                        SetConst(id, c, value);
                        goto Next;
                    }
                    case OpCode.GetMCnst:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        var mod = registers[a];
                        var name = irep.Symbols[b];
                        registers[a] = GetConst(name, mod.As<RClass>());
                        goto Next;
                    }
                    case OpCode.SetMCnst:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        var value = registers[a];
                        var mod = registers[a + 1];
                        var name = irep.Symbols[b];
                        SetConst(name, mod.As<RClass>(), value);
                        goto Next;
                    }
                    case OpCode.GetIdx:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        var valueA = registers[a];
                        var valueB = registers[a + 1];
                        switch (valueA.Object)
                        {
                            case RArray array when valueB.IsInteger:
                                registers[a] = array[(int)valueB.IntegerValue];
                                goto Next;
                            case RHash hash:
                                registers[a] = hash[valueB];
                                goto Next;
                            case RString s:
                                switch (valueB.VType)
                                {
                                    case MRubyVType.Integer:
                                    case MRubyVType.String:
                                    case MRubyVType.Range:
                                        var substr = s.GetAref(valueB);
                                        registers[a] = substr != null
                                            ? MRubyValue.From(substr)
                                            : MRubyValue.Nil;
                                        break;
                                }
                                break;
                        }

                        // Jump to send :[]
                        registers[a + 2] = MRubyValue.Nil; // push nil after arguments
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
                        callInfo.ReadOperand(sequence, out byte a);
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
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b, out byte c);
                        var env = callInfo.Proc?.FindUpperEnvTo(c);
                        if (env != null && b < env.Stack.Length)
                        {
                            registers[a] = env.Stack.Span[b];
                        }
                        else
                        {
                            registers[a] = MRubyValue.Nil;
                        }
                        goto Next;
                    }
                    case OpCode.SetUpVar:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b, out byte c);
                        var env = callInfo.Proc?.FindUpperEnvTo(c);
                        if (env != null && b < env.Stack.Length)
                        {
                            env.Stack.Span[b] = registers[a];
                        }
                        goto Next;
                    }
                    case OpCode.Jmp:
                    {
                        callInfo.ReadOperand(sequence, out short a);
                        callInfo.ProgramCounter += a;
                        goto Next;
                    }
                    case OpCode.JmpIf:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out short b);
                        if (registers[a].Truthy)
                        {
                            callInfo.ProgramCounter += b;
                        }
                        goto Next;
                    }
                    case OpCode.JmpNot:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out short b);
                        if (registers[a].Falsy)
                        {
                            callInfo.ProgramCounter += b;
                        }
                        goto Next;
                    }
                    case OpCode.JmpNil:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out short b);
                        if (registers[a].IsNil)
                        {
                            callInfo.ProgramCounter += b;
                        }
                        goto Next;
                    }
                    case OpCode.JmpUw:
                    {
                        callInfo.ReadOperand(sequence, out short a);
                        var newProgramCounter = callInfo.ProgramCounter + a;
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
                    {
                        callInfo.ReadOperand(sequence, out byte a);

                        registers[a] = Exception switch
                        {
                            MRubyRaiseException x => MRubyValue.From(x.ExceptionObject),
                            MRubyBreakException x => MRubyValue.From(x.BreakObject),
                            _ => MRubyValue.Nil
                        };
                        Exception = null;
                        goto Next;
                    }
                    case OpCode.Rescue:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        var exceptionObjectValue = registers[a];
                        var exceptionClassValue = registers[b];
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

                        var c = exceptionClassValue.As<RClass>();
                        registers[b] = MRubyValue.From(KindOf(exceptionObjectValue, c));
                        goto Next;
                    }
                    case OpCode.RaiseIf:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
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
                        callInfo.ReadOperand(sequence, out var a, out var b, out var c);

                        var currentStackPointer = callInfo.StackPointer;

                        callInfo = ref context.PushCallStack();
                        callInfo.CallerType = CallerType.InVmLoop;
                        callInfo.StackPointer = currentStackPointer + a;
                        callInfo.MethodId = irep.Symbols[b];
                        callInfo.ArgumentCount = (byte)(c & 0xf);
                        callInfo.KeywordArgumentCount = (byte)((c >> 4) & 0xf);

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
                        var self = context.Stack[callInfo.StackPointer];
                        var receiverClass = opcode == OpCode.Super
                            ? (RClass)callInfo.Scope // set RClass.Super in OpCode.Super
                            : ClassOf(self);
                        var methodId = callInfo.MethodId;
                        if (!TryFindMethod(receiverClass, methodId, out var method, out _))
                        {
                            method = PrepareMethodMissing(ref callInfo, self, methodId,
                                opcode == OpCode.Super
                                    ? () =>  Raise(Names.NoMethodError, NewString($"no superclass method '{NameOf(methodId)}' for {StringifyAny(self)}"))
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
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
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

                        registers[a] = receiver;

                        // Jump to send
                        var nextStackPointer = callInfo.StackPointer + a;
                        callInfo = ref context.PushCallStack();
                        callInfo.CallerType = CallerType.InVmLoop;
                        callInfo.Scope = targetClass.Super;
                        callInfo.StackPointer = nextStackPointer;
                        callInfo.MethodId = methodId;
                        callInfo.ArgumentCount = (byte)(b & 0xf);
                        callInfo.KeywordArgumentCount = (byte)((b >> 4) & 0xf);
                        goto case OpCode.SendInternal;
                    }
                    case OpCode.Enter:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b, out byte c);
                        var bits = (uint)a << 16 | (uint)b << 8 | c;
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
                                        argc++;    // include kdict in normal arguments
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
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        // mrb_value k = mrb_symbol_value(irep->syms[b]);
                        var key = MRubyValue.From(irep.Symbols[b]);
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

                        registers[a] = value;
                        kdict.As<RHash>().Delete(key);
                        goto Next;
                    }
                    case OpCode.KeyP:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        var key = MRubyValue.From(irep.Symbols[b]);
                        var kdict = registers[callInfo.KeywordArgumentOffset];
                        registers[a] = MRubyValue.From(kdict.As<RHash>().TryGetValue(key, out _));
                        goto Next;
                    }
                    case OpCode.KeyEnd:
                    {
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
                        callInfo.ReadOperand(sequence, out byte a);
                        var returnValue = registers[a];
                        if (TryReturnJump(ref callInfo, context.CallDepth, registers[a]))
                        {
                            goto JumpAndNext;
                        }
                        return returnValue;
                    }
                    case OpCode.ReturnBlk:
                    {
                        if (callInfo.Proc?.HasFlag(MRubyObjectFlags.ProcStrict) == true ||
                            callInfo.Proc?.Scope is not REnv)
                        {
                            goto case OpCode.Return;
                        }

                        callInfo.ReadOperand(sequence, out byte a);
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
                        if (callInfo.Proc is { } x && x.HasFlag(MRubyObjectFlags.ProcStrict))
                        {
                            goto case OpCode.Return;
                        }

                        callInfo.ReadOperand(sequence, out byte a);
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
                        callInfo.ReadOperand(sequence, out byte a, out short b);
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
                        registers[a] = block;
                        goto Next;
                    }
                    case OpCode.Add:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        var lhs = registers[a];
                        var rhs = registers[a + 1];
                        switch (lhs.VType, rhs.VType)
                        {
                            case (MRubyVType.Integer, MRubyVType.Integer):
                                // TODO: overflow handling
                                registers[a] = NewInteger(lhs.IntegerValue + rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Integer, MRubyVType.Float):
                                registers[a] = MRubyValue.From(lhs.IntegerValue + rhs.FloatValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Integer):
                                registers[a] = MRubyValue.From(lhs.FloatValue + rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Float):
                                registers[a] = MRubyValue.From(lhs.FloatValue + rhs.FloatValue);
                                goto Next;
                            case (MRubyVType.String, MRubyVType.String):
                                registers[a] = MRubyValue.From(lhs.As<RString>() + rhs.As<RString>());
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
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        var lhs = registers[a];
                        switch (lhs.VType)
                        {
                            case MRubyVType.Integer:
                                registers[a] = NewInteger(lhs.IntegerValue + b);
                                goto Next;
                            case MRubyVType.Float:
                                registers[a] = MRubyValue.From(lhs.FloatValue + b);
                                goto Next;
                        }

                        // Jump to send :+
                        registers[a + 1] = MRubyValue.From(b);
                        var nextStackPointer = callInfo.StackPointer + a;
                        callInfo = ref context.PushCallStack();
                        callInfo.CallerType = CallerType.InVmLoop;
                        callInfo.StackPointer = nextStackPointer;
                        callInfo.MethodId = Names.OpAdd;
                        callInfo.ArgumentCount = 1;
                        callInfo.KeywordArgumentCount = 0;
                        goto case OpCode.SendInternal;
                    }
                    case OpCode.Sub:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        var lhs = registers[a];
                        var rhs = registers[a + 1];
                        switch (lhs.VType, rhs.VType)
                        {
                            case (MRubyVType.Integer, MRubyVType.Integer):
                                // TODO: overflow handling
                                registers[a] = NewInteger(lhs.IntegerValue - rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Integer, MRubyVType.Float):
                                registers[a] = MRubyValue.From(lhs.IntegerValue - rhs.FloatValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Integer):
                                registers[a] = MRubyValue.From(lhs.FloatValue - rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Float):
                                registers[a] = MRubyValue.From(lhs.FloatValue - rhs.FloatValue);
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
                    case OpCode.SubI:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        var lhs = registers[a];
                        switch (lhs.VType)
                        {
                            case MRubyVType.Integer:
                                registers[a] = NewInteger(lhs.IntegerValue - b);
                                goto Next;
                            case MRubyVType.Float:
                                registers[a] = MRubyValue.From(lhs.FloatValue - b);
                                goto Next;
                        }

                        // Jump to send :-
                        registers[a + 1] = MRubyValue.From(b);

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
                        callInfo.ReadOperand(sequence, out byte a);
                        var lhs = registers[a];
                        var rhs = registers[a + 1];
                        switch (lhs.VType, rhs.VType)
                        {
                            case (MRubyVType.Integer, MRubyVType.Integer):
                                // TODO: overflow handling
                                registers[a] = NewInteger(lhs.IntegerValue * rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Integer, MRubyVType.Float):
                                registers[a] = MRubyValue.From(lhs.IntegerValue * rhs.FloatValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Integer):
                                registers[a] = MRubyValue.From(lhs.FloatValue * rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Float):
                                registers[a] = MRubyValue.From(lhs.FloatValue * rhs.FloatValue);
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
                        callInfo.ReadOperand(sequence, out byte a);
                        var lhs = registers[a];
                        var rhs = registers[a + 1];
                        switch (lhs.VType, rhs.VType)
                        {
                            case (MRubyVType.Integer, MRubyVType.Integer):
                                // TODO: overflow handling
                                registers[a] = NewInteger(lhs.IntegerValue / rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Integer, MRubyVType.Float):
                                registers[a] = MRubyValue.From(lhs.IntegerValue / rhs.FloatValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Integer):
                                registers[a] = MRubyValue.From(lhs.FloatValue / rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Float):
                                registers[a] = MRubyValue.From(lhs.FloatValue / rhs.FloatValue);
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
                        callInfo.ReadOperand(sequence, out byte a);
                        var lhs = registers[a];
                        var rhs = registers[a + 1];
                        if (lhs.Equals(rhs))
                        {
                            registers[a] = MRubyValue.True;
                            goto Next;
                        }
                        if (lhs.IsSymbol)
                        {
                            registers[a] = MRubyValue.False;
                            goto Next;
                        }
                        switch (lhs.VType, rhs.VType)
                        {
                            case (MRubyVType.Integer, MRubyVType.Integer):
                                registers[a] = MRubyValue.From(lhs.IntegerValue == rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Integer, MRubyVType.Float):
                                registers[a] = MRubyValue.From(lhs.IntegerValue == (long)rhs.FloatValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Integer):
                                registers[a] = MRubyValue.From((long)lhs.FloatValue == rhs.IntegerValue);
                                goto Next;
                            // ReSharper disable once CompareOfFloatsByEqualityOperator
                            case (MRubyVType.Float, MRubyVType.Float):
                                registers[a] = MRubyValue.From(lhs.FloatValue == rhs.FloatValue);
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
                        callInfo.ReadOperand(sequence, out byte a);
                        var lhs = registers[a];
                        var rhs = registers[a + 1];

                        switch (lhs.VType, rhs.VType)
                        {
                            case (MRubyVType.Integer, MRubyVType.Integer):
                                registers[a] = MRubyValue.From(lhs.IntegerValue < rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Integer, MRubyVType.Float):
                                registers[a] = MRubyValue.From(lhs.IntegerValue < (long)rhs.FloatValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Integer):
                                registers[a] = MRubyValue.From((long)lhs.FloatValue < rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Float):
                                registers[a] = MRubyValue.From(lhs.FloatValue < rhs.FloatValue);
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
                        callInfo.ReadOperand(sequence, out byte a);
                        var lhs = registers[a];
                        var rhs = registers[a + 1];

                        switch (lhs.VType, rhs.VType)
                        {
                            case (MRubyVType.Integer, MRubyVType.Integer):
                                registers[a] = MRubyValue.From(lhs.IntegerValue <= rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Integer, MRubyVType.Float):
                                registers[a] = MRubyValue.From(lhs.IntegerValue <= (long)rhs.FloatValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Integer):
                                registers[a] = MRubyValue.From((long)lhs.FloatValue <= rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Float):
                                registers[a] = MRubyValue.From(lhs.FloatValue <= rhs.FloatValue);
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
                        callInfo.ReadOperand(sequence, out byte a);
                        var lhs = registers[a];
                        var rhs = registers[a + 1];

                        switch (lhs.VType, rhs.VType)
                        {
                            case (MRubyVType.Integer, MRubyVType.Integer):
                                registers[a] = MRubyValue.From(lhs.IntegerValue > rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Integer, MRubyVType.Float):
                                registers[a] = MRubyValue.From(lhs.IntegerValue > (long)rhs.FloatValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Integer):
                                registers[a] = MRubyValue.From((long)lhs.FloatValue > rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Float):
                                registers[a] = MRubyValue.From(lhs.FloatValue > rhs.FloatValue);
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
                        callInfo.ReadOperand(sequence, out byte a);
                        var lhs = registers[a];
                        var rhs = registers[a + 1];

                        switch (lhs.VType, rhs.VType)
                        {
                            case (MRubyVType.Integer, MRubyVType.Integer):
                                registers[a] = MRubyValue.From(lhs.IntegerValue >= rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Integer, MRubyVType.Float):
                                registers[a] = MRubyValue.From(lhs.IntegerValue >= (long)rhs.FloatValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Integer):
                                registers[a] = MRubyValue.From((long)lhs.FloatValue >= rhs.IntegerValue);
                                goto Next;
                            case (MRubyVType.Float, MRubyVType.Float):
                                registers[a] = MRubyValue.From(lhs.FloatValue >= rhs.FloatValue);
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
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        var values = registers.Slice(a, b);
                        registers[a] = MRubyValue.From(NewArray(values));
                        goto Next;
                    }
                    case OpCode.Array2:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b, out byte c);
                        var values = registers.Slice(b, c);
                        registers[a] = MRubyValue.From(NewArray(values));
                        goto Next;
                    }
                    case OpCode.AryCat:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        var splat = SplatArray(registers[a + 1]);
                        if (registers[a].IsNil)
                        {
                            registers[a] = splat;
                        }
                        else
                        {
                            EnsureValueType(registers[a], MRubyVType.Array);
                            var array = registers[a].As<RArray>();
                            array.Concat(splat.As<RArray>());
                        }
                        goto Next;
                    }
                    case OpCode.ARef:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b, out byte c);
                        var v = registers[b];
                        if (v.VType == MRubyVType.Array)
                        {
                            registers[a] = v.As<RArray>()[c];
                        }
                        else
                        {
                            if (c == 0)
                            {
                                registers[a] = v;
                            }
                            else
                            {
                                registers[a] = MRubyValue.Nil;
                            }
                        }
                        goto Next;
                    }
                    case OpCode.ASet:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b, out byte c);
                        var array = registers[b].As<RArray>();
                        array[c] = registers[a];
                        goto Next;
                    }
                    case OpCode.APost:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte pre, out byte post);
                        var v = registers[a];
                        if (v.Object is not RArray array)
                        {
                            array = NewArray(registers[a]);
                        }

                        if (array.Length > pre + post)
                        {
                            var slice = array.AsSpan().Slice(pre, array.Length - pre - post);
                            registers[a++] = MRubyValue.From(NewArray(slice));
                            while (post-- > 0)
                            {
                                registers[a++] = array[array.Length - post - 1];
                            }
                        }
                        else
                        {
                            registers[a++] = MRubyValue.From(NewArray(0));
                            int i;
                            for (i = 0; i + pre < array.Length; i++)
                            {
                                registers[a + i] = array[pre + i];
                            }
                            while (i < post)
                            {
                                registers[a + i] = MRubyValue.Nil;
                                i++;
                            }
                        }
                        goto Next;
                    }
                    case OpCode.AryPush:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        var v = registers[a];
                        EnsureNotFrozen(v);

                        var array = v.As<RArray>();
                        for (var i = 0; i < b; i++)
                        {
                            array.Push(registers[a + i + 1]);
                        }
                        goto Next;
                    }
                    case OpCode.ArySplat:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        registers[a] = SplatArray(registers[a]);
                        goto Next;
                    }
                    case OpCode.Intern:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        registers[a] = MRubyValue.From(Intern(registers[a].As<RString>()));
                        goto Next;
                    }
                    case OpCode.Symbol:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        var name = irep.PoolValues[b].As<RString>();
                        registers[a] = MRubyValue.From(Intern(name));
                        goto Next;
                    }
                    case OpCode.String:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        var str = irep.PoolValues[b].As<RString>();
                        registers[a] = MRubyValue.From(str.Dup());
                        goto Next;
                    }
                    case OpCode.StrCat:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        var v1 = registers[a].As<RString>();
                        var v2 = Stringify(registers[a + 1]);
                        v1.Concat(v2);
                        goto Next;
                    }
                    case OpCode.Hash:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        var hash = NewHash(b);
                        var lastIndex = a + b * 2;
                        for (var i = a; i < lastIndex; i += 2)
                        {
                            var key = registers[i];
                            var value = registers[i + 1];
                            hash.Add(key, value);
                        }
                        registers[a] = MRubyValue.From(hash);
                        goto Next;
                    }
                    case OpCode.HashAdd:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        var hashValue = registers[a];
                        var lastIndex = a + b * 2;

                        EnsureValueType(hashValue, MRubyVType.Hash);
                        var hash = hashValue.As<RHash>();
                        for (var i = a; i < lastIndex; i += 2)
                        {
                            var key = registers[i];
                            var value = registers[i + 1];
                            hash.Add(key, value);
                        }
                        goto Next;
                    }
                    case OpCode.HashCat:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        var lhs = registers[a];
                        var rhs = registers[a + 1];
                        EnsureNotFrozen(lhs);
                        lhs.As<RHash>().Merge(rhs.As<RHash>());
                        goto Next;
                    }
                    case OpCode.Lambda:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        var proc = NewClosure(irep.Children[b]);
                        proc.SetFlag(MRubyObjectFlags.ProcStrict);
                        registers[a] = MRubyValue.From(proc);
                        goto Next;
                    }
                    case OpCode.Block:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        var proc = NewClosure(irep.Children[b]);
                        registers[a] = MRubyValue.From(proc);
                        goto Next;
                    }
                    case OpCode.Method:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        var proc = NewProc(irep.Children[b]);
                        proc.SetFlag(MRubyObjectFlags.ProcStrict);
                        proc.SetFlag(MRubyObjectFlags.ProcScope);
                        registers[a] = MRubyValue.From(proc);
                        goto Next;
                    }
                    case OpCode.RangeInc:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        var begin = registers[a];
                        var end = registers[a + 1];
                        var range = new RRange(begin, end, false, RangeClass);
                        registers[a] = MRubyValue.From(range);
                        goto Next;
                    }
                    case OpCode.RangeExc:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        var begin = registers[a];
                        var end = registers[a + 1];
                        var range = new RRange(begin, end, true, RangeClass);
                        registers[a] = MRubyValue.From(range);
                        goto Next;
                    }
                    case OpCode.OClass:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        registers[a] = MRubyValue.From(ObjectClass);
                        goto Next;
                    }
                    case OpCode.Class:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        var id = irep.Symbols[b];
                        var outer = registers[a];
                        var super = registers[a + 1];

                        var outerClass = outer.IsNil
                            ? callInfo.Proc?.Scope?.TargetClass ?? ObjectClass
                            : outer.As<RClass>();

                        // mrb_vm_define_class
                        RClass? superClass = null;
                        RClass definedClass;
                        if (!super.IsNil)
                        {
                            if (super.Object is RClass s)
                            {
                                superClass = s;
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
                        registers[a] = MRubyValue.From(definedClass);
                        goto Next;
                    }
                    case OpCode.Module:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        var id = irep.Symbols[b];
                        var outer = registers[a];
                        var outerClass = outer.IsNil
                            ? callInfo.Proc?.Scope?.TargetClass ?? ObjectClass
                            : outer.As<RClass>();

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
                        registers[a] = MRubyValue.From(definedModule);
                        goto Next;
                    }
                    case OpCode.Exec:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        var receiver = registers[a];
                        var targetIrep = irep.Children[b];

                        // prepare closure
                        var proc = NewProc(targetIrep, receiver.As<RClass>());
                        proc.SetFlag(MRubyObjectFlags.ProcScope);

                        // prepare callstack
                        ref var nextCallInfo = ref context.PushCallStack();
                        nextCallInfo.StackPointer = callInfo.StackPointer + a;
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
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        var target = registers[a].As<RClass>();
                        var proc = registers[a + 1].As<RProc>();
                        var methodId = irep.Symbols[b];

                        DefineMethod(target, methodId, new MRubyMethod(proc));
                        MethodAddedHook(target, methodId);
                        registers[a] = MRubyValue.From(methodId);
                        goto Next;
                    }
                    case OpCode.Alias:
                    {
                        callInfo.ReadOperand(sequence, out byte a, out byte b);
                        var c = callInfo.Scope.TargetClass;
                        var newMethodId = irep.Symbols[a];
                        var oldMethodId = irep.Symbols[b];
                        AliasMethod(c, newMethodId, oldMethodId);
                        MethodAddedHook(c, newMethodId);
                        goto Next;
                    }
                    case OpCode.Undef:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        var c = callInfo.Scope.TargetClass;
                        var methodId = irep.Symbols[a];
                        UndefMethod(c, methodId);
                        goto Next;
                    }
                    case OpCode.SClass:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        var result = SingletonClassOf(registers[a]);
                        registers[a] = result != null ? MRubyValue.From(result) : MRubyValue.Nil;
                        goto Next;
                    }
                    case OpCode.TClass:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        registers[a] = MRubyValue.From(callInfo.Scope.TargetClass);
                        goto Next;
                    }
                    case OpCode.Err:
                    {
                        callInfo.ReadOperand(sequence, out byte a);
                        var message = irep.PoolValues[a];
                        Raise(Names.LocalJumpError, message.As<RString>());
                        goto Next;
                    }
                    case OpCode.Stop:
                    {
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
                context.PopCallStack();
                callInfo = ref context.CurrentCallInfo;
                if (callInfo.CallerType == CallerType.VmExecuted)
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
        Action? raise = null)
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
                raise();
            }
            else
            {
                RaiseMethodMissing(methodId, receiver, MRubyValue.From(NewArray(args)));
            }
        }
    }
}