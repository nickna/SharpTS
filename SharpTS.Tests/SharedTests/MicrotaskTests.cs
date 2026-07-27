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
        // queueMicrotask should execute the callback
        var source = @"
            let executed = false;
            queueMicrotask(() => { executed = true; });
            // Small delay to allow microtask execution
            let start = Date.now();
            while (Date.now() - start < 50) { }
            console.log(executed);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void QueueMicrotask_ExecutesCallbackWithConsoleLog(ExecutionMode mode)
    {
        // queueMicrotask should execute callback that logs output
        var source = @"
            queueMicrotask(() => { console.log('microtask executed'); });
            // Small delay to allow microtask execution
            let start = Date.now();
            while (Date.now() - start < 50) { }
            console.log('done');
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("microtask executed", output);
        Assert.Contains("done", output);
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
            setTimeout(() => { order.push('timeout'); }, 0);
            queueMicrotask(() => { order.push('microtask'); });
            // Wait for both to execute
            let start = Date.now();
            while (Date.now() - start < 100) { }
            console.log(order.join(','));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("microtask,timeout\n", output);
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
            // Wait for all to execute
            let start = Date.now();
            while (Date.now() - start < 50) { }
            console.log(order.join(','));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("first,second,third\n", output);
    }

    [Theory, ModeData]
    public void QueueMicrotask_NestedMicrotasksExecuteBeforeMacrotasks(ExecutionMode mode)
    {
        // Microtasks queued from within microtasks should still execute before macrotasks
        var source = @"
            let order: string[] = [];
            setTimeout(() => { order.push('timeout'); }, 0);
            queueMicrotask(() => {
                order.push('microtask1');
                queueMicrotask(() => { order.push('nested'); });
            });
            // Wait for all to execute
            let start = Date.now();
            while (Date.now() - start < 100) { }
            console.log(order.join(','));
        ";
        var output = TestHarness.Run(source, mode);
        // Nested microtask should still run before timeout
        Assert.Equal("microtask1,nested,timeout\n", output);
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
            queueMicrotask(() => { result = value; });
            // Wait for microtask
            let start = Date.now();
            while (Date.now() - start < 50) { }
            console.log(result);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("captured\n", output);
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
            // Wait for all microtasks
            let start = Date.now();
            while (Date.now() - start < 50) { }
            console.log(counter);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    #endregion
}
