// See https://aka.ms/new-console-template for more information


using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using MRubyD;
using MRubyD.Compiler;
using MRubyD.Internals;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

using JitInspect;
MRubyState mRubyDState = MRubyState.Create();


var compiler = MRubyCompiler.Create(mRubyDState);

var arrayBufferWriter = new ArrayBufferWriter<byte>();

var fileIrep = compiler.Compile(ReadBytes("test.rb"));
Dump (fileIrep);
mRubyDState.Exec(fileIrep);
Console.WriteLine(mRubyDState.Exec(fileIrep));


Dump(mRubyDState.IntegerClass.MethodTable[mRubyDState.Intern("times"u8)].Proc!.Irep!);

Console.WriteLine(Encoding.UTF8.GetString(arrayBufferWriter.WrittenSpan));
File.WriteAllBytes(GetAbsolutePath("dump.text"),arrayBufferWriter.WrittenSpan);

var newJIitPath = GetAbsolutePath("jit.text");
var lastJitPath = GetAbsolutePath("jit_last.text");

if(File.Exists(newJIitPath))
{
    if(File.Exists(lastJitPath)){
        File.Delete(lastJitPath);
    }
    File.Move(newJIitPath,lastJitPath);
    Console.WriteLine("Last:" + File.ReadAllLines(lastJitPath)[^1]);
}
var method = typeof(MRubyState).GetMethod("Exec", BindingFlags.Instance | BindingFlags.NonPublic,[typeof(Irep),typeof(int),typeof(int)])!;
using var disassembler = JitDisassembler.Create();
var nextJitText = disassembler.Disassemble(method);
File.WriteAllText(newJIitPath,nextJitText);
Console.WriteLine("New:" + nextJitText.Split("\n")[^1]);

void Dump(Irep irep)
{
    mRubyDState.CodeDump(irep, arrayBufferWriter);
    foreach (var child in irep.Children)
    {
        Dump(child);
    }
}


static string GetAbsolutePath(string relativePath, [CallerFilePath] string callerFilePath = "")
{
    return Path.Join(Path.GetDirectoryName(callerFilePath)!, relativePath);
}

byte[] ReadBytes(string fileName)
{
    var path = GetAbsolutePath(Path.Join("ruby", fileName));
    return File.ReadAllBytes(path);
}