using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for queueMicrotask().
/// Microtasks execute at the end of the current task, before any macrotasks (setTimeout/setInterval).
/// </summary>
[Collection("TimerTests")]
public class MicrotaskTests
{
    #region Basic Functionality Tests

    [Theory, ModeData]
    public void QueueMicrotask_ExecutesCallback(ExecutionMode mode)
    {
        // The callback observes its mutation after synchronous code has finished.
        var source = @"
            let executed = false;
            queueMicrotask(() => {
                executed = true;
                console.log(executed);
            });
            console.log(executed);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\ntrue\n", output);
    }

    [Theory, ModeData]
    public void QueueMicrotask_ExecutesCallbackWithConsoleLog(ExecutionMode mode)
    {
        // queueMicrotask should execute callback that logs output
        var source = @"
            queueMicrotask(() => { console.log('microtask executed'); });
            console.log('done');
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("done\nmicrotask executed\n", output);
    }

    [Theory, ModeData]
    public void QueueMicrotask_ReturnsUndefined(ExecutionMode mode)
    {
        // queueMicrotask should return undefined
        var source = @"
            let result = queueMicrotask(() => {});
            console.log(result === undefined);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Ordering Tests - Microtasks Before Macrotasks

    [Theory, ModeData]
    public void QueueMicrotask_ExecutesBeforeSetTimeout(ExecutionMode mode)
    {
        // Microtasks should execute before macrotasks (setTimeout with 0 delay)
        var source = @"
            let order: string[] = [];
            setTimeout(() => {
                order.push('timeout');
                console.log(order.join(','));
            }, 0);
            queueMicrotask(() => { order.push('microtask'); });
            console.log(order.length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\nmicrotask,timeout\n", output);
    }

    [Theory, ModeData]
    public void QueueMicrotask_ExecutesInFIFOOrder(ExecutionMode mode)
    {
        // Multiple microtasks should execute in FIFO order
        var source = @"
            let order: string[] = [];
            queueMicrotask(() => { order.push('first'); });
            queueMicrotask(() => { order.push('second'); });
            queueMicrotask(() => { order.push('third'); });
            queueMicrotask(() => { console.log(order.join(',')); });
            console.log(order.length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\nfirst,second,third\n", output);
    }

    [Theory, ModeData]
    public void QueueMicrotask_NestedMicrotasksExecuteBeforeMacrotasks(ExecutionMode mode)
    {
        // Microtasks queued from within microtasks should still execute before macrotasks
        var source = @"
            let order: string[] = [];
            setTimeout(() => {
                order.push('timeout');
                console.log(order.join(','));
            }, 0);
            queueMicrotask(() => {
                order.push('microtask1');
                queueMicrotask(() => { order.push('nested'); });
            });
            console.log(order.length);
        ";
        var output = TestHarness.Run(source, mode);
        // Nested microtask should still run before timeout
        Assert.Equal("0\nmicrotask1,nested,timeout\n", output);
    }

    #endregion

    #region Type Checking Tests

    [Theory, ModeData]
    public void QueueMicrotask_RequiresCallback(ExecutionMode mode)
    {
        // queueMicrotask without callback should fail type checking
        var source = @"
            queueMicrotask();
        ";
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("queueMicrotask", ex.Message);
    }

    [Theory, ModeData]
    public void QueueMicrotask_CallbackMustBeFunction(ExecutionMode mode)
    {
        // queueMicrotask with non-function callback should fail type checking
        var source = @"
            queueMicrotask('not a function');
        ";
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("function", ex.Message.ToLower());
    }

    #endregion

    #region Variable Capture Tests

    [Theory, ModeData]
    public void QueueMicrotask_CapturesClosureVariables(ExecutionMode mode)
    {
        // Callback should capture closure variables correctly
        var source = @"
            let value = 'captured';
            let result = '';
            queueMicrotask(() => {
                result = value;
                console.log(result);
            });
            value = 'updated';
            console.log(result === '');
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nupdated\n", output);
    }

    [Theory, ModeData]
    public void QueueMicrotask_MutatesClosureVariables(ExecutionMode mode)
    {
        // Callback should be able to mutate closure variables
        var source = @"
            let counter = 0;
            queueMicrotask(() => { counter++; });
            queueMicrotask(() => { counter++; });
            queueMicrotask(() => { counter++; });
            queueMicrotask(() => { console.log(counter); });
            console.log(counter);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n3\n", output);
    }

    #endregion
}
