using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for setTimeout, setInterval, clearTimeout, clearInterval, and Timeout object methods.
/// Timer tests run in a dedicated collection to avoid race conditions with other tests.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Important:</strong> In interpreted mode, timers keep the event loop alive by default.
/// In compiled mode, programs exit when <c>Main()</c> returns, regardless of pending timers.
/// </para>
/// <para>
/// <strong>Note:</strong> All tests now run in both interpreted and compiled modes.
/// The compiler's closure mutation propagation has been fixed to properly handle
/// captured variable modifications in nested closures.
/// </para>
/// </remarks>
[Collection("TimerTests")]
public class TimerTests
{
    #region setTimeout Basic Tests

    [Theory, ModeData]
    public void SetTimeout_ReturnsTimeout(ExecutionMode mode)
    {
        // setTimeout should return a Timeout object
        var source = @"
            let t = setTimeout(() => {}, 100);
            console.log(typeof t);
            console.log(t.toString().startsWith('Timeout'));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\ntrue\n", output);
    }

    [Theory, ModeData]
    public void SetTimeout_ZeroDelay_ExecutesCallback_Interpreted(ExecutionMode mode)
    {
        // setTimeout with 0 delay should still execute (interpreted: check variable)
        var source = @"
            let executed = false;
            setTimeout(() => { executed = true; }, 0);
            // Spin until the callback fires; the deadline only bounds a genuine failure
            let start = Date.now();
            while (!executed && Date.now() - start < 5000) { }
            console.log(executed);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void SetTimeout_ZeroDelay_ExecutesCallback_Compiled(ExecutionMode mode)
    {
        // setTimeout with 0 delay should execute callback (compiled: check console output)
        var source = @"
            let fired = false;
            setTimeout(() => { console.log('executed'); fired = true; }, 0);
            // Spin until the callback fires; the deadline only bounds a genuine failure
            let start = Date.now();
            while (!fired && Date.now() - start < 5000) { }
            console.log('done');
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("executed", output);
        Assert.Contains("done", output);
    }

    [Theory, ModeData]
    public void SetTimeout_DefaultDelay_IsZero_Interpreted(ExecutionMode mode)
    {
        // setTimeout without delay should default to 0 (interpreted)
        var source = @"
            let executed = false;
            setTimeout(() => { executed = true; });
            let start = Date.now();
            while (!executed && Date.now() - start < 5000) { }
            console.log(executed);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void SetTimeout_DefaultDelay_ExecutesCallback_Compiled(ExecutionMode mode)
    {
        // setTimeout without delay should default to 0 and execute (compiled)
        var source = @"
            let fired = false;
            setTimeout(() => { console.log('executed'); fired = true; });
            let start = Date.now();
            while (!fired && Date.now() - start < 5000) { }
            console.log('done');
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("executed", output);
        Assert.Contains("done", output);
    }

    [Theory, ModeData]
    public void SetTimeout_KeepsEventLoopAlive(ExecutionMode mode)
    {
        // In interpreted mode, the event loop should stay alive for timers by default
        var source = @"
            setTimeout(() => { console.log('executed'); }, 10);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("executed\n", output);
    }

    [Theory, ModeData]
    public void SetTimeout_CallbackCallingDateNow_DoesNotReenterTimerProcessing(ExecutionMode mode)
    {
        var source = @"
            let calls = 0;
            setTimeout(() => {
                calls++;
                console.log(Date.now() >= 0);
            }, 0);

            const started = Date.now();
            while (calls === 0 && Date.now() - started < 5000) { }
            console.log(calls);
        ";

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n1\n", output);
    }

    [Theory, ModeData]
    public void SetTimeout_Unref_AllowsExit_Interpreted(ExecutionMode mode)
    {
        // unref() drops a timer's hold on the event loop: the program exits once
        // the *ref'd* work is done, without waiting for (or running) the unref'd
        // timer. A ref'd short timer fires "alive" and keeps the loop alive until
        // it does; the unref'd timer is scheduled far in the future and must never
        // fire — its only hold on the loop was removed by unref(), so the program
        // exits long before it is due.
        //
        // This is a positive, load-independent assertion (anti-flake doctrine): the
        // ref'd timer fires whenever it is due, so there is no wall-clock race, and
        // the unref'd timer's far-future delay means it cannot fire within any
        // plausible exit window regardless of CI load. The earlier version asserted
        // empty output for a 10ms unref'd timer, which flaked when a slow/loaded
        // runner's startup outran the 10ms and the shutdown drain fired the now-due
        // timer before the exit check.
        var source = @"
            setTimeout(() => { console.log('should not run'); }, 60000).unref();
            setTimeout(() => { console.log('alive'); }, 10);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("alive\n", output);
    }

    #endregion

    #region clearTimeout Tests

    [Theory, ModeData]
    public void ClearTimeout_PreventsExecution_Interpreted(ExecutionMode mode)
    {
        // clearTimeout should prevent callback from executing (interpreted: check variable)
        var source = @"
            let executed = false;
            let t = setTimeout(() => { executed = true; }, 100);
            clearTimeout(t);
            // Wait longer than the timeout delay
            let start = Date.now();
            while (Date.now() - start < 200) { }
            console.log(executed);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory, ModeData]
    public void ClearTimeout_PreventsExecution_Compiled(ExecutionMode mode)
    {
        // clearTimeout should prevent callback from executing (compiled: check console output)
        var source = @"
            let t = setTimeout(() => { console.log('should not run'); }, 100);
            clearTimeout(t);
            // Wait longer than the timeout delay
            let start = Date.now();
            while (Date.now() - start < 200) { }
            console.log('done');
        ";
        var output = TestHarness.Run(source, mode);
        Assert.DoesNotContain("should not run", output);
        Assert.Contains("done", output);
    }

    [Theory, ModeData]
    public void ClearTimeout_Null_DoesNotThrow(ExecutionMode mode)
    {
        // clearTimeout(null) should not throw
        var source = @"
            clearTimeout(null);
            console.log('ok');
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("ok\n", output);
    }

    [Theory, ModeData]
    public void ClearTimeout_Undefined_DoesNotThrow(ExecutionMode mode)
    {
        // clearTimeout(undefined) should not throw
        var source = @"
            clearTimeout(undefined);
            console.log('ok');
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("ok\n", output);
    }

    [Theory, ModeData]
    public void ClearTimeout_NoArgs_DoesNotThrow(ExecutionMode mode)
    {
        // clearTimeout() with no args should not throw
        var source = @"
            clearTimeout();
            console.log('ok');
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("ok\n", output);
    }

    #endregion

    #region ref/unref Tests

    [Theory, ModeData]
    public void Timeout_Ref_ReturnsSameObject(ExecutionMode mode)
    {
        // ref() should return the same Timeout object for chaining
        var source = @"
            let t = setTimeout(() => {}, 100);
            let t2 = t.ref();
            console.log(t === t2);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Timeout_Unref_ReturnsSameObject(ExecutionMode mode)
    {
        // unref() should return the same Timeout object for chaining
        var source = @"
            let t = setTimeout(() => {}, 100);
            let t2 = t.unref();
            console.log(t === t2);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Timeout_HasRef_DefaultsToTrue(ExecutionMode mode)
    {
        // hasRef should default to true
        var source = @"
            let t = setTimeout(() => {}, 100);
            console.log(t.hasRef);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Timeout_Unref_SetsHasRefFalse(ExecutionMode mode)
    {
        // unref() should set hasRef to false
        var source = @"
            let t = setTimeout(() => {}, 100);
            t.unref();
            console.log(t.hasRef);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory, ModeData]
    public void Timeout_RefAfterUnref_SetsHasRefTrue(ExecutionMode mode)
    {
        // ref() after unref() should set hasRef back to true
        var source = @"
            let t = setTimeout(() => {}, 100);
            t.unref();
            t.ref();
            console.log(t.hasRef);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void ClearTimeout_AfterTimeoutFired_DoesNotReleaseAnotherLiveHandle(ExecutionMode mode)
    {
        var source = @"
            let fired: any = setTimeout(() => {}, 0);
            setTimeout(() => {
                clearTimeout(fired);
                setTimeout(() => console.log('survived'), 20);
            }, 20);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("survived\n", output);
    }

    [Theory, ModeData]
    public void Timeout_MethodChaining(ExecutionMode mode)
    {
        // ref/unref should support method chaining
        var source = @"
            let t = setTimeout(() => {}, 100).unref().ref();
            console.log(t.hasRef);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region setTimeout with Arguments Tests

    [Theory, ModeData]
    public void SetTimeout_PassesArgsToCallback_Interpreted(ExecutionMode mode)
    {
        // Additional args should be passed to callback (interpreted: captured variable)
        var source = @"
            let result: any = '';
            setTimeout((a: any, b: any) => { result = a + b; }, 0, 'hello', 'world');
            let start = Date.now();
            while (result === '' && Date.now() - start < 5000) { }
            console.log(result);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("helloworld\n", output);
    }

    [Theory, ModeData]
    public void SetTimeout_PassesArgsToCallback_Compiled(ExecutionMode mode)
    {
        // Additional args should be passed to callback (compiled: console.log)
        var source = @"
            let fired = false;
            setTimeout((a: any, b: any) => { console.log(a + b); fired = true; }, 0, 'hello', 'world');
            let start = Date.now();
            while (!fired && Date.now() - start < 5000) { }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("helloworld", output);
    }

    #endregion

    #region Type Checking Tests

    [Theory, ModeData]
    public void SetTimeout_RequiresCallback(ExecutionMode mode)
    {
        // setTimeout without callback should fail type checking
        var source = @"
            setTimeout();
        ";
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("setTimeout", ex.Message);
    }

    [Theory, ModeData]
    public void SetTimeout_CallbackMustBeFunction(ExecutionMode mode)
    {
        // setTimeout with non-function callback should fail type checking
        var source = @"
            setTimeout('not a function', 100);
        ";
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("function", ex.Message.ToLower());
    }

    [Theory, ModeData]
    public void SetTimeout_DelayMustBeNumber(ExecutionMode mode)
    {
        // setTimeout with non-number delay should fail type checking
        var source = @"
            setTimeout(() => {}, 'not a number');
        ";
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("number", ex.Message.ToLower());
    }

    #endregion

    #region setInterval Basic Tests

    [Theory, ModeData]
    public void SetInterval_ReturnsTimeout(ExecutionMode mode)
    {
        // setInterval should return a Timeout object (same as setTimeout)
        var source = @"
            let t = setInterval(() => {}, 100);
            console.log(typeof t);
            console.log(t.toString().startsWith('Timeout'));
            clearInterval(t);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\ntrue\n", output);
    }

    [Theory, ModeData]
    public void SetInterval_ExecutesMultipleTimes_Interpreted(ExecutionMode mode)
    {
        // setInterval should execute multiple times (interpreted: count variable)
        // Spin until 3 ticks land; a fixed wall-clock window is not CPU time on loaded CI runners
        var source = @"
            let count = 0;
            let t = setInterval(() => { count++; }, 20);
            let start = Date.now();
            while (count < 3 && Date.now() - start < 5000) { }
            clearInterval(t);
            console.log(count >= 3);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void SetInterval_ExecutesCallback_Compiled(ExecutionMode mode)
    {
        // setInterval should execute callback (compiled: console.log and self-clear)
        var source = @"
            let fired = false;
            let t = setInterval(() => {
                console.log('tick');
                clearInterval(t);
                console.log('done');
                fired = true;
            }, 20);
            // Spin until the callback completes; the deadline only bounds a genuine failure
            let start = Date.now();
            while (!fired && Date.now() - start < 5000) { }
            console.log('timeout');
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("tick", output);
        Assert.Contains("done", output);
        // Verify 'done' comes after 'tick' (callback completed properly)
        Assert.True(output.IndexOf("tick") < output.IndexOf("done"),
            "Expected 'done' to appear after 'tick' in output");
    }

    [Theory, ModeData]
    public void ClearInterval_StopsExecution_Interpreted(ExecutionMode mode)
    {
        // clearInterval should stop the interval (interpreted: count variable)
        var source = @"
            let count = 0;
            let t = setInterval(() => { count++; }, 20);
            let start = Date.now();
            while (Date.now() - start < 50) { }
            clearInterval(t);
            let afterClear = count;
            start = Date.now();
            while (Date.now() - start < 100) { }
            console.log(count === afterClear);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void ClearInterval_StopsExecution_Compiled(ExecutionMode mode)
    {
        // clearInterval should stop the interval from executing (compiled: console.log)
        var source = @"
            let t = setInterval(() => { console.log('should not appear after clear'); }, 100);
            clearInterval(t);
            let start = Date.now();
            while (Date.now() - start < 200) { }
            console.log('done');
        ";
        var output = TestHarness.Run(source, mode);
        Assert.DoesNotContain("should not appear after clear", output);
        Assert.Contains("done", output);
    }

    [Theory, ModeData]
    public void SetInterval_PassesArgsToCallback_Interpreted(ExecutionMode mode)
    {
        // Additional args should be passed to callback (interpreted: captured variable)
        var source = @"
            let result: any = '';
            let t = setInterval((a: any, b: any) => { result = a + b; }, 10, 'hello', 'world');
            let start = Date.now();
            while (result === '' && Date.now() - start < 5000) { }
            clearInterval(t);
            console.log(result);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("helloworld\n", output);
    }

    [Theory, ModeData]
    public void SetInterval_PassesArgsToCallback_Compiled(ExecutionMode mode)
    {
        // Additional args should be passed to callback (compiled: console.log)
        var source = @"
            let fired = false;
            let t = setInterval((a: any, b: any) => { console.log(a + b); fired = true; }, 10, 'hello', 'world');
            let start = Date.now();
            while (!fired && Date.now() - start < 5000) { }
            clearInterval(t);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("helloworld", output);
    }

    [Theory, ModeData]
    public void SetInterval_DefaultDelay_Interpreted(ExecutionMode mode)
    {
        // setInterval without delay should default to 0 (interpreted)
        var source = @"
            let executed = false;
            let t = setInterval(() => { executed = true; });
            let start = Date.now();
            while (!executed && Date.now() - start < 5000) { }
            clearInterval(t);
            console.log(executed);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void SetInterval_DefaultDelay_Compiled(ExecutionMode mode)
    {
        // setInterval without delay should default to 0 and execute (compiled)
        var source = @"
            let fired = false;
            let t = setInterval(() => { console.log('executed'); fired = true; });
            let start = Date.now();
            while (!fired && Date.now() - start < 5000) { }
            clearInterval(t);
            console.log('done');
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("executed", output);
        Assert.Contains("done", output);
    }

    #endregion

    #region clearInterval Tests

    [Theory, ModeData]
    public void ClearInterval_Null_DoesNotThrow(ExecutionMode mode)
    {
        // clearInterval(null) should not throw
        var source = @"
            clearInterval(null);
            console.log('ok');
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("ok\n", output);
    }

    [Theory, ModeData]
    public void ClearInterval_Undefined_DoesNotThrow(ExecutionMode mode)
    {
        // clearInterval(undefined) should not throw
        var source = @"
            clearInterval(undefined);
            console.log('ok');
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("ok\n", output);
    }

    [Theory, ModeData]
    public void ClearInterval_NoArgs_DoesNotThrow(ExecutionMode mode)
    {
        // clearInterval() with no args should not throw
        var source = @"
            clearInterval();
            console.log('ok');
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("ok\n", output);
    }

    #endregion

    #region setInterval ref/unref Tests

    [Theory, ModeData]
    public void Interval_Ref_ReturnsSameObject(ExecutionMode mode)
    {
        // ref() should return the same object for chaining
        var source = @"
            let t = setInterval(() => {}, 100);
            let t2 = t.ref();
            console.log(t === t2);
            clearInterval(t);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Interval_Unref_ReturnsSameObject(ExecutionMode mode)
    {
        // unref() should return the same object for chaining
        var source = @"
            let t = setInterval(() => {}, 100);
            let t2 = t.unref();
            console.log(t === t2);
            clearInterval(t);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Interval_HasRef_DefaultsToTrue(ExecutionMode mode)
    {
        // hasRef should default to true
        var source = @"
            let t = setInterval(() => {}, 100);
            console.log(t.hasRef);
            clearInterval(t);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region setInterval Type Checking Tests

    [Theory, ModeData]
    public void SetInterval_RequiresCallback(ExecutionMode mode)
    {
        // setInterval without callback should fail type checking
        var source = @"
            setInterval();
        ";
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("setInterval", ex.Message);
    }

    [Theory, ModeData]
    public void SetInterval_CallbackMustBeFunction(ExecutionMode mode)
    {
        // setInterval with non-function callback should fail type checking
        var source = @"
            setInterval('not a function', 100);
        ";
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("function", ex.Message.ToLower());
    }

    [Theory, ModeData]
    public void SetInterval_DelayMustBeNumber(ExecutionMode mode)
    {
        // setInterval with non-number delay should fail type checking
        var source = @"
            setInterval(() => {}, 'not a number');
        ";
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("number", ex.Message.ToLower());
    }

    #endregion

    #region Promise + setTimeout Integration Tests

    [Theory, ModeData]
    public void Await_Promise_SetTimeout_ResolvesCallback(ExecutionMode mode)
    {
        // await new Promise(r => setTimeout(r, N)) — the canonical delay-async pattern
        var source = @"
            async function run() {
                console.log('before');
                await new Promise<void>(r => { setTimeout(r, 10); });
                console.log('after');
            }
            run();
            console.log('top-end');
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("before", output);
        Assert.Contains("after", output);
        Assert.Contains("top-end", output);
    }

    [Theory, ModeData]
    public void Await_Promise_SetTimeout_ZeroDelay(ExecutionMode mode)
    {
        // await new Promise(r => setTimeout(r, 0)) — zero-delay variant
        var source = @"
            async function run() {
                console.log('start');
                await new Promise<void>(r => { setTimeout(r, 0); });
                console.log('end');
            }
            run();
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("start", output);
        Assert.Contains("end", output);
    }

    #endregion
}
