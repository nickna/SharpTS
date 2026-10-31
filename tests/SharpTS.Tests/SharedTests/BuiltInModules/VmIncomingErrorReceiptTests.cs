using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmIncomingErrorReceiptTests
{
    private static object? Run(string source, object context, Interpreter parent) =>
        VmModuleInterpreter.WithScriptOrigin(new("input-origin.js", 0, 0), () =>
            VmModuleInterpreter.ExecuteInNewContext(source, context, parent));

    [Fact]
    public void ThrowingAnActualIncomingErrorDoesNotAcquireAnOwnerReceipt()
    {
        using var parent = new Interpreter(new StringWriter(), new StringWriter());
        var input = new SharpTSReferenceError("incoming") { Stack = "caller-origin" };
        var context = new SharpTSObject(new Dictionary<string, object?> { ["input"] = input });
        var thrown = Assert.Throws<ThrowException>(() => Run("throw input", context, parent));
        Assert.Same(input, thrown.Value);
        Assert.Equal("caller-origin", input.Stack);
        Assert.False(VmModuleInterpreter.IsVmErrorValue(input));
        Assert.True(VmModuleInterpreter.IsVmInputValue(input));
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ReadVmErrorProperty(input, "name"));
    }

    [Fact]
    public void RetainingAnIncomingErrorDoesNotRetainTheCompletedEvaluator()
    {
        var (context, evaluator) = EvaluateAndRelease();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(evaluator.IsAlive);
        GC.KeepAlive(context);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (SharpTSObject Context, WeakReference Evaluator) EvaluateAndRelease()
    {
        WeakReference? observed = null;
        var context = new SharpTSObject(new Dictionary<string, object?>
        {
            ["input"] = new SharpTSReferenceError("incoming"),
            ["capture"] = BuiltInMethod.CreateV2("capture", 0, (interpreter, _, _) =>
            { observed = new WeakReference(interpreter); return RuntimeValue.Undefined; })
        });
        using var parent = new Interpreter(new StringWriter(), new StringWriter());
        Assert.Throws<ThrowException>(() => Run("capture();throw input", context, parent));
        return (context, Assert.IsType<WeakReference>(observed));
    }

    [Fact]
    public void PreviouslyProducedErrorKeepsItsOriginalOwnerWhenThrownAsInput()
    {
        using var first = new Interpreter(new StringWriter(), new StringWriter());
        using var second = new Interpreter(new StringWriter(), new StringWriter());
        var original = Run("new Error('original')", new SharpTSObject(new Dictionary<string, object?>()), first)!;
        var context = new SharpTSObject(new Dictionary<string, object?> { ["input"] = original });
        var thrown = Assert.Throws<ThrowException>(() => Run("throw input", context, second));
        Assert.Same(original, thrown.Value);
        second.Dispose();
        Assert.Equal("Error", VmModuleInterpreter.ReadVmErrorProperty(original, "name"));
        first.Dispose();
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ReadVmErrorProperty(original, "name"));
    }
}
