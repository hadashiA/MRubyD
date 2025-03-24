using System.IO;

namespace MRubyD.Benchmark;

using System.Runtime.CompilerServices;

static class FileHelper
{
    public static string GetAbsolutePath(string relativePath, [CallerFilePath] string callerFilePath = "")
    {
        return Path.Combine(Path.GetDirectoryName(callerFilePath)!, relativePath);
    }
}