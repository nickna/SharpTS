using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using RuntimeArchitecture = System.Runtime.InteropServices.Architecture;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ProcessExecutionHostTests
{
    [Theory, ModeData]
    public void GlobalImportedAndAliasedPropertiesDescribeExecutionHost(ExecutionMode mode)
    {
        var platform = OperatingSystem.IsWindows() ? "win32" : OperatingSystem.IsLinux() ? "linux"
            : OperatingSystem.IsMacOS() ? "darwin" : "unknown";
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            RuntimeArchitecture.X64 => "x64", RuntimeArchitecture.X86 => "ia32", RuntimeArchitecture.Arm64 => "arm64",
            RuntimeArchitecture.Arm => "arm", _ => "unknown"
        };
        var output = TestHarness.RunModules(new() { ["main.ts"] = """
            import * as processModule from "node:process";
            const alias: any = process;
            console.log(process.platform, process.arch);
            console.log(processModule.platform, processModule.arch);
            console.log(alias.platform, alias.arch);
            """ }, "main.ts", mode);
        Assert.Equal(string.Concat(Enumerable.Repeat($"{platform} {architecture}\n", 3)), output);
    }

    [Fact]
    public void SavedProcessGettersQueryTheExecutionHost()
    {
        var assembly = Assembly.Load(TestHarness.CompileUncheckedAndVerify("console.log(process.platform, process.arch);"));
        var process = assembly.GetType("$Process", throwOnError: true)!;
        AssertFirstCall(process.GetMethod("get_Platform")!, "IsWindows");
        AssertFirstCall(process.GetMethod("get_Arch")!, "get_ProcessArchitecture");
    }

    private static void AssertFirstCall(MethodInfo method, string expected)
    {
        var bytes = method.GetMethodBody()!.GetILAsByteArray()!;
        Assert.Equal((byte)OpCodes.Call.Value, bytes[0]);
        Assert.Equal(expected, method.Module.ResolveMethod(BitConverter.ToInt32(bytes, 1))!.Name);
    }
}
