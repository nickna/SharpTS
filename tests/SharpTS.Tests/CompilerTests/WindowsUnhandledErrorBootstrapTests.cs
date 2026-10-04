using System.Runtime.InteropServices;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

[Collection("ExternalProcessTests")]
public sealed class WindowsUnhandledErrorBootstrapTests
{
    [Fact]
    public void EmbeddingHostRetainsWindowsErrorModeAndConsoleEncoding()
    {
        if (!OperatingSystem.IsWindows()) return;
        uint mode = GetErrorMode();
        var encoding = Console.OutputEncoding;
        Assert.Equal("7\n", TestHarness.Run("console.log(7);", ExecutionMode.Compiled));
        Assert.Equal(mode, GetErrorMode());
        Assert.Same(encoding, Console.OutputEncoding);
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetErrorMode();
}
