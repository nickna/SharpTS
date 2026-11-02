using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmPromiseHandlingReceiptTests
{
    [Fact]
    public void UnrecordedPromiseCannotAcquireVmHandlingAuthority()
    {
        var promise = new SharpTSPromise(Task.FromResult<object?>(7d));
        VmModuleInterpreter.MarkVmPromiseRejectionHandled(promise);
        Assert.False(promise.IsRejectionHandled);
        _ = promise.GetValueAsync();
        Assert.False(VmModuleInterpreter.IsVmPromiseRejectionHandled(promise));
        Assert.Throws<ArgumentException>(() =>
            VmModuleInterpreter.RegisterVmPromiseRejectionHandler(promise, () => { }));
    }

    [Fact]
    public void ActualConsumedPromiseNotifiesCurrentAndLateReceiptsOnce()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var run = Assert.IsAssignableFrom<ISharpTSCallable>(VmModuleInterpreter.GetExports()["runInNewContext"]);
        var promise = Assert.IsType<SharpTSPromise>(run.Call(caller, ["Promise.resolve(7)"]));
        Assert.False(VmModuleInterpreter.IsVmPromiseRejectionHandled(promise));
        var current = 0;
        VmModuleInterpreter.RegisterVmPromiseRejectionHandler(promise, () => current++);
        _ = promise.GetValueAsync();
        _ = promise.GetValueAsync();
        Assert.Equal(1, current);
        var late = 0;
        VmModuleInterpreter.RegisterVmPromiseRejectionHandler(promise, () => late++);
        Assert.Equal(1, late);
        Assert.True(VmModuleInterpreter.IsVmPromiseRejectionHandled(promise));
    }

    [Fact]
    public void MarkingAnActualMirrorConsumerReusesItsRecordedOwner()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var run = Assert.IsAssignableFrom<ISharpTSCallable>(VmModuleInterpreter.GetExports()["runInNewContext"]);
        var promise = Assert.IsType<SharpTSPromise>(run.Call(caller, ["Promise.resolve(7)"]));
        var handled = 0;
        VmModuleInterpreter.RegisterVmPromiseRejectionHandler(promise, () => handled++);
        VmModuleInterpreter.MarkVmPromiseRejectionHandled(promise);
        VmModuleInterpreter.MarkVmPromiseRejectionHandled(promise);
        Assert.True(VmModuleInterpreter.IsVmPromiseRejectionHandled(promise));
        Assert.Equal(1, handled);
    }
}
