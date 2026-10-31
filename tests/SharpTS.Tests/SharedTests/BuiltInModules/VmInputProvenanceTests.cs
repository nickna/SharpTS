using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmInputProvenanceTests
{
    [Theory]
    [InlineData("callable")]
    [InlineData("promise")]
    [InlineData("object")]
    public void AConvertedInputKeepsItsActualIdentityWithoutAcquiringAnEvaluatorOwner(string kind)
    {
        var input = new object();
        object converted = kind switch
        {
            "callable" => BuiltInMethod.CreateV2("converted", 0, (_, _, _) => RuntimeValue.Undefined),
            "promise" => new SharpTSPromise(Task.FromResult<object?>(7d)),
            _ => new SharpTSObject(new Dictionary<string, object?> { ["value"] = 7d })
        };
        using var host = (IDisposable)VmModuleInterpreter.CreateExecutionHost(_ => { }, _ => { }, _ => { }, () => { }, () => { }, () => { });
        VmModuleInterpreter.ConfigureExecutionHostValueAdapters(host, value => value,
            value => ReferenceEquals(value, input) ? converted : value);
        using var scope = VmModuleInterpreter.EnterExecutionHost(host);
        var run = Assert.IsAssignableFrom<ISharpTSCallable>(VmModuleInterpreter.GetExports()["runInNewContext"]);
        var context = new SharpTSObject(new Dictionary<string, object?> { ["input"] = input });
        Assert.Same(converted, run.Call(null!, ["input", context]));
        Assert.False(VmModuleInterpreter.IsVmValue(converted));
        if (converted is SharpTSPromise promise)
        {
            Assert.True(VmModuleInterpreter.IsVmPromiseValue(promise));
            Assert.Same(promise.Task, VmModuleInterpreter.GetVmPromiseTask(promise));
        }
    }

    [Fact]
    public void AConvertedPreviouslyOwnedCallbackKeepsItsOriginalReceipt()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var sourceRun = Assert.IsAssignableFrom<ISharpTSCallable>(VmModuleInterpreter.GetExports()["runInNewContext"]);
        var callback = Assert.IsAssignableFrom<ISharpTSCallable>(sourceRun.Call(caller, ["()=>7"]));
        var input = new object();
        using var host = (IDisposable)VmModuleInterpreter.CreateExecutionHost(_ => { }, _ => { }, _ => { }, () => { }, () => { }, () => { });
        VmModuleInterpreter.ConfigureExecutionHostValueAdapters(host, value => value,
            value => ReferenceEquals(value, input) ? callback : value);
        using var scope = VmModuleInterpreter.EnterExecutionHost(host);
        var run = Assert.IsAssignableFrom<ISharpTSCallable>(VmModuleInterpreter.GetExports()["runInNewContext"]);
        var context = new SharpTSObject(new Dictionary<string, object?> { ["input"] = input });
        Assert.Same(callback, run.Call(null!, ["input", context]));
        Assert.True(VmModuleInterpreter.IsVmValue(callback));
        Assert.Equal(7d, VmModuleInterpreter.InvokeVmCallable(callback, SharpTSUndefined.Instance, []));
    }

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
