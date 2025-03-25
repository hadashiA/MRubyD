// See https://aka.ms/new-console-template for more information


using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using MRubyD;
using MRubyD.Compiler;
using MRubyD.Internals;

MRubyState mRubyDState = MRubyState.Create();


var compiler = MRubyCompiler.Create(mRubyDState);

var arrayBufferWriter = new ArrayBufferWriter<byte>();

var fileIrep = compiler.Compile(ReadBytes("fib.rb"));
Dump ( fileIrep);

Console.WriteLine(mRubyDState.Exec(fileIrep));


Dump(mRubyDState.IntegerClass.MethodTable[mRubyDState.Intern("times"u8)].Proc!.Irep!);

void Dump(Irep irep)
{
    mRubyDState.CodeDump(irep, arrayBufferWriter);

    Console.WriteLine(Encoding.UTF8.GetString(arrayBufferWriter.WrittenSpan));
    arrayBufferWriter.Clear();
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