using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmRuntimeThrownErrorReceiptTests
{
    internal static object? Run(string source, object context, Interpreter? parent, bool parsed) =>
        VmModuleInterpreter.WithScriptOrigin(new("runtime-origin.js", 0, 0), () => parsed
            ? VmModuleInterpreter.ExecuteParsedInNewContext(TestHarness.ParseOrThrow(source), context, parent)
            : VmModuleInterpreter.ExecuteInNewContext(source, context, parent));

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RuntimeThrownErrorKeepsItsActualOwnerAndOrigin(bool parsed, bool afterEvaluate)
    {
        using var parent = new Interpreter(new StringWriter(), new StringWriter());
        var context = VmContext.Create(new SharpTSObject(new Dictionary<string, object?> { ["value"] = 1d }),
            afterEvaluate ? new VmContextOptions(MicrotaskMode: "afterEvaluate") : null);
        var thrown = Assert.Throws<ThrowException>(() => Run("value;let value=9", context, parent, parsed));
        Assert.IsType<SharpTSReferenceError>(thrown.Value);
        Assert.True(VmModuleInterpreter.IsVmErrorValue(thrown.Value));
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.Equal("ReferenceError", VmModuleInterpreter.ReadVmErrorProperty(thrown.Value!, "name"));
        Assert.Contains("runtime-origin.js", Assert.IsType<string>(VmModuleInterpreter.ReadVmErrorProperty(thrown.Value!, "stack")));
        Assert.Equal(0, parent.ActiveHandleCount);
        parent.Dispose();
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ReadVmErrorProperty(thrown.Value!, "name"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AfterEvaluateLeavesItsLocalCheckpointPendingWhenEvaluationThrows(bool parsed)
    {
        using var parent = new Interpreter(new StringWriter(), new StringWriter());
        var context = new SharpTSObject(new Dictionary<string, object?> { ["value"] = 1d });
        VmContext.Create(context, new VmContextOptions(MicrotaskMode: "afterEvaluate"));
        var thrown = Assert.Throws<ThrowException>(() => Run(
            "Promise.resolve().then(()=>{globalThis.mark=7});value;let value=9", context, parent, parsed));
        Assert.IsType<SharpTSUndefined>(context.GetProperty("mark"));
        Assert.Equal("ReferenceError", VmModuleInterpreter.ReadVmErrorProperty(thrown.Value!, "name"));
        Assert.Equal(0, parent.ActiveHandleCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnOwnedHostRetainsTheErrorWithoutACallerInterpreter(bool parsed)
    {
        var jobs = new Queue<Action>();
        using var host = (VmExecutionHost)VmModuleInterpreter.CreateExecutionHost(
            jobs.Enqueue, jobs.Enqueue, jobs.Enqueue, static () => { }, static () => { }, static () => { });
        using var scope = host.Enter();
        var context = VmContext.Create(new SharpTSObject(new Dictionary<string, object?>()));
        var thrown = Assert.Throws<ThrowException>(() => Run("value;let value=9", context, null, parsed));
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.Equal("ReferenceError", VmModuleInterpreter.ReadVmErrorProperty(thrown.Value!, "name"));
    }

    [Fact]
    public void RethrowingAnExistingRuntimeErrorPreservesItsCreationOrigin()
    {
        using var first = new Interpreter(new StringWriter(), new StringWriter());
        using var second = new Interpreter(new StringWriter(), new StringWriter());
        var original = Assert.Throws<ThrowException>(() => Run("value;let value=9",
            new SharpTSObject(new Dictionary<string, object?>()), first, false)).Value!;
        string stack = Assert.IsType<string>(VmModuleInterpreter.ReadVmErrorProperty(original, "stack"));
        var context = new SharpTSObject(new Dictionary<string, object?> { ["input"] = original });
        var thrown = Assert.Throws<ThrowException>(() => VmModuleInterpreter.WithScriptOrigin(new("rethrow.js", 0, 0),
            () => VmModuleInterpreter.ExecuteInNewContext("throw input", context, second)));
        Assert.Same(original, thrown.Value);
        Assert.Equal(stack, VmModuleInterpreter.ReadVmErrorProperty(original, "stack"));
    }
}
