using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace SharpTS.Tests.Infrastructure;

[Collection("HarnessTimeoutTests")]
public class TestHarnessTimeoutTests
{
    [Fact]
    public async Task CompilationIsInsideDeadlineAndLateCompilerRunsCleanup()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("LateCompilerProbe"), AssemblyBuilderAccess.Run);
        var program = assembly.DefineDynamicModule("Main").DefineType("$Program", TypeAttributes.Public);
        var invoked = program.DefineField("Invoked", typeof(bool), FieldAttributes.Public | FieldAttributes.Static);
        var main = program.DefineMethod("Main", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        var il = main.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stsfld, invoked);
        il.Emit(OpCodes.Ret);
        var programType = program.CreateType()!;
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string> run = Task.Run(() => TestHarness.RunCompiledWithTimeout(() =>
        {
            entered.SetResult();
            release.Wait();
            return assembly;
        }, TimeSpan.FromMilliseconds(100), () => cleaned.SetResult()));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<TimeoutException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(run.IsCompleted, "The harness deadline must finish before the test watchdog.");
            Assert.False(cleaned.Task.IsCompleted);
        }
        finally { release.Set(); }
        await cleaned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False((bool)programType.GetField("Invoked")!.GetValue(null)!);
        Assert.Equal("after\n", TestHarness.RunCompiled("console.log('after');"));
    }

    [Fact]
    public void RunCompiled_InfiniteLoop_TimesOutAndDoesNotPoisonNextRun()
    {
        Assert.Throws<TimeoutException>(() =>
            TestHarness.RunCompiled("while (true) {}", TimeSpan.FromSeconds(1)));
        Assert.Equal("after\n", TestHarness.RunCompiled("console.log('after');"));
    }

    [Fact]
    public void RunInterpreted_InfiniteLoop_TimesOutAndDoesNotPoisonNextRun()
    {
        var stopwatch = Stopwatch.StartNew();

        Assert.Throws<TimeoutException>(() =>
            TestHarness.RunInterpreted("while (true) {}", TimeSpan.FromMilliseconds(100)));

        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromMilliseconds(75), TimeSpan.FromSeconds(3));
        Assert.Equal("after\n", TestHarness.RunInterpreted("console.log('after');"));
    }

    [Fact]
    public void RunModulesInterpreted_InfiniteLoop_TimesOutAndDoesNotPoisonNextRun()
    {
        var files = new Dictionary<string, string> { ["./main.ts"] = "while (true) {}" };
        var stopwatch = Stopwatch.StartNew();

        Assert.Throws<TimeoutException>(() => TestHarness.RunModules(
            files, "./main.ts", ExecutionMode.Interpreted, TimeSpan.FromMilliseconds(100)));

        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromMilliseconds(75), TimeSpan.FromSeconds(3));
        Assert.Equal("after\n", TestHarness.RunModules(
            new Dictionary<string, string> { ["./main.ts"] = "console.log('after');" },
            "./main.ts",
            ExecutionMode.Interpreted));
    }
}

[CollectionDefinition("HarnessTimeoutTests", DisableParallelization = true)]
public class HarnessTimeoutTestsCollection
{
}
