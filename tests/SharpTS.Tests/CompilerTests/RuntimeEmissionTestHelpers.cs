using System.Reflection.Emit;
using SharpTS.Compilation;

namespace SharpTS.Tests.CompilerTests;

/// <summary>Persists an emitted runtime for lifecycle and IL-verification fixtures.</summary>
internal static class RuntimeEmissionTestHelpers
{
    internal static MemoryStream Save(EmittedRuntime runtime)
    {
        var bytes = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeType.Assembly).Save(bytes);
        bytes.Position = 0;
        return bytes;
    }
}
