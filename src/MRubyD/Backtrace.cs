using System;
using System.Collections.Generic;
using System.Text;
using MRubyD.Internals;

namespace MRubyD;

public struct BacktraceLocation
{
    public Symbol MethodId;
    public Irep? Irep;
    public int Index;
}

public class Backtrace
{
    public IReadOnlyList<BacktraceLocation> Entries => entries;
    readonly List<BacktraceLocation> entries;

    Backtrace(List<BacktraceLocation> entries)
    {
        this.entries = entries;
    }

    // TODO: Use debug info
    public RArray ToRArray(MRubyState state)
    {
        var array = state.NewArray(64);
        foreach (var entry in entries)
        {
            if (entry.MethodId == default) continue;
            var methodName = state.NameOf(entry.MethodId);
            var line = state.NewString($"{methodName} in byte sequence: {entry.Index}");
            array.Push(MRubyValue.From(line));
        }
        return array;
    }

    public string ToString(MRubyState state)
    {
        var array = ToRArray(state);
        var result = "";
        var first = true;
        foreach (var line in array.AsSpan())
        {
            result += Encoding.UTF8.GetString(line.As<RString>().AsSpan());
            result += Environment.NewLine;
        }
        return result;
    }

    internal static Backtrace Capture(MRubyContext context)
    {
        var entries = new List<BacktraceLocation>();

        for (var i = context.CallDepth; i >= 0; i--)
        {
            ref var callInfo = ref context.CallStack[i];

            var location = new BacktraceLocation
            {
                MethodId = callInfo.MethodId,
            };

            if (callInfo.Proc is IrepProc irepProc)
            {
                location.Irep = irepProc.Irep;
                location.Index = irepProc.ProgramCounter;
            }
            entries.Add(location);
        }
        return new Backtrace(entries);
    }
}