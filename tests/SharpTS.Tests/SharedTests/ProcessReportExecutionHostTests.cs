using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ProcessReportExecutionHostTests
{
    [Theory, ModeData]
    public void GlobalImportedAndAliasedReportsDescribeTheExecutionHost(ExecutionMode mode)
    {
        var output = TestHarness.RunModules(new() { ["main.ts"] = """
            import * as processModule from 'node:process';
            const alias:any=process;
            for(const report of [process.report.getReport(),processModule.report.getReport(),alias.report.getReport()]){
                const header=report.header;
                console.log(header.arch===process.arch,header.platform===process.platform,typeof header.wordSize,header.wordSize);
            }
            """ }, "main.ts", mode);
        Assert.Equal(string.Concat(Enumerable.Repeat($"true true number {IntPtr.Size * 8}\n", 3)), output);
    }

    [Fact]
    public void SavedReportQueriesArchitecturePlatformAndPointerSizeAtExecutionTime()
    {
        var assembly = Assembly.Load(TestHarness.CompileUncheckedAndVerify("process.report.getReport();"));
        var report = assembly.GetType("$Runtime")!.GetMethod("ProcessBuildReport")!;
        var calls = ReadCalls(report).ToArray();
        Assert.Contains(calls, method => method.DeclaringType == typeof(RuntimeInformation)
            && method.Name == "get_ProcessArchitecture");
        foreach (string platform in new[] { "IsWindows", "IsLinux", "IsMacOS" })
            Assert.Contains(calls, method => method.DeclaringType == typeof(OperatingSystem) && method.Name == platform);
        Assert.Contains(calls, method => method.DeclaringType == typeof(IntPtr) && method.Name == "get_Size");
    }

    private static IEnumerable<MethodBase> ReadCalls(MethodBase method)
    {
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(opcode => unchecked((ushort)opcode.Value));
        var bytes = method.GetMethodBody()!.GetILAsByteArray()!;
        for (int offset = 0; offset < bytes.Length;)
        {
            ushort value = bytes[offset++];
            if (value == 0xfe) value = (ushort)(0xfe00 | bytes[offset++]);
            var opcode = opcodes[value];
            if (opcode == OpCodes.Call || opcode == OpCodes.Callvirt)
                yield return method.Module.ResolveMethod(BitConverter.ToInt32(bytes, offset))!;
            offset += opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, offset),
                _ => 4
            };
        }
    }
}
