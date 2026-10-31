using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmInputProvenanceTests
{
    [Fact]
    public void AnInputPromiseKeepsTaskTransportWithoutAcquiringAnEvaluatorOwner()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var input = new SharpTSPromise(Task.FromResult<object?>(7d));
        var context = new SharpTSObject(new Dictionary<string, object?> { ["input"] = input });
        var run = Assert.IsAssignableFrom<ISharpTSCallable>(VmModuleInterpreter.GetExports()["runInNewContext"]);
        Assert.Same(input, run.Call(caller, ["input", context]));
        Assert.False(VmModuleInterpreter.IsVmValue(input));
        Assert.True(VmModuleInterpreter.IsVmPromiseValue(input));
        Assert.Same(input.Task, VmModuleInterpreter.GetVmPromiseTask(input));
    }

    [Fact]
    public void AnActualInputCallbackDoesNotBecomeVmOwnedOnPublication()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var input = BuiltInMethod.CreateV2("input", 0, (_, _, _) => RuntimeValue.Undefined);
        var context = new SharpTSObject(new Dictionary<string, object?> { ["input"] = input });
        var run = Assert.IsAssignableFrom<ISharpTSCallable>(VmModuleInterpreter.GetExports()["runInNewContext"]);
        Assert.False(VmModuleInterpreter.IsVmValue(input));
        Assert.Same(input, run.Call(caller, ["input", context]));
        Assert.False(VmModuleInterpreter.IsVmValue(input));
    }

    [Fact]
    public void AnAlreadyVmProducedCallbackKeepsItsOriginalReceiptWhenPassedAsInput()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var run = Assert.IsAssignableFrom<ISharpTSCallable>(VmModuleInterpreter.GetExports()["runInNewContext"]);
        var callback = Assert.IsAssignableFrom<ISharpTSCallable>(run.Call(caller, ["()=>7"]));
        Assert.True(VmModuleInterpreter.IsVmValue(callback));
        var context = new SharpTSObject(new Dictionary<string, object?> { ["callback"] = callback });
        Assert.Same(callback, run.Call(caller, ["callback", context]));
        Assert.True(VmModuleInterpreter.IsVmValue(callback));
        Assert.Equal(7d, VmModuleInterpreter.InvokeVmCallable(callback, SharpTSUndefined.Instance, []));
    }
}
