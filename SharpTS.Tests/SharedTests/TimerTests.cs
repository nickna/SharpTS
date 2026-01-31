using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for setTimeout, setInterval, clearTimeout, clearInterval, and Timeout object methods.
/// Timer tests run in a dedicated collection to avoid race conditions with other tests.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Important: Timers do NOT keep the process alive.</strong>
/// Unlike Node.js where timers with <c>.ref()</c> (the default) keep the event loop running,
/// SharpTS compiled programs exit when <c>Main()</c> returns, regardless of pending timers.
/// </para>
/// <para>
/// <strong>Closure Limitation (Compiled Mode):</strong>
/// Tests that modify captured variables may not work in compiled mode due to a pre-existing
/// compiler closure limitation (captured variable mutations don't propagate to outer scope).
/// Such tests are marked InterpretedOnly.
/// </para>
/// </remarks>
[Collection("TimerTests")]
public class TimerTests
{
    #region setTimeout Basic Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void SetTimeout_ZeroDelay_ExecutesCallback_Interpreted(ExecutionMode mode)
    {
        // setTimeout with 0 delay should still execute (interpreted: check variable)
        var source = @"
            let executed = false;
            setTimeout(() => { executed = true; }, 0);
            // Small delay to allow async execution
            let start = Date.now();
            while (Date.now() - start < 50) { }
            console.log(executed);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void SetTimeout_ZeroDelay_ExecutesCallback_Compiled(ExecutionMode mode)
    {
        // setTimeout with 0 delay should execute callback (compiled: check console output)
        var source = @"
            setTimeout(() => { console.log('executed'); }, 0);
            // Busy wait to allow async execution
            let start = Date.now();
            while (Date.now() - start < 100) { }
            console.log('done');
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("executed", output);
        Assert.Contains("done", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void SetTimeout_DefaultDelay_IsZero_Interpreted(ExecutionMode mode)
    {
        // setTimeout without delay should default to 0 (interpreted)
        var source = @"
            let executed = false;
            setTimeout(() => { executed = true; });
            let start = Date.now();
            while (Date.now() - start < 50) { }
            console.log(executed);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void SetTimeout_DefaultDelay_ExecutesCallback_Compiled(ExecutionMode mode)
    {
        // setTimeout without delay should default to 0 and execute (compiled)
        var source = @"
            setTimeout(() => { console.log('executed'); });
            let start = Date.now();
            while (Date.now() - start < 100) { }
            console.log('done');
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("executed", output);
        Assert.Contains("done", output);
    }

    #endregion

    #region clearTimeout Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void SetTimeout_PassesArgsToCallback_Interpreted(ExecutionMode mode)
    {
        // Additional args should be passed to callback (interpreted: captured variable)
        var source = @"
            let result: any = '';
            setTimeout((a: any, b: any) => { result = a + b; }, 0, 'hello', 'world');
            let start = Date.now();
            while (Date.now() - start < 50) { }
            console.log(result);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("helloworld\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void SetTimeout_PassesArgsToCallback_Compiled(ExecutionMode mode)
    {
        // Additional args should be passed to callback (compiled: console.log)
        var source = @"
            setTimeout((a: any, b: any) => { console.log(a + b); }, 0, 'hello', 'world');
            let start = Date.now();
            while (Date.now() - start < 100) { }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("helloworld", output);
    }

    #endregion

    #region Type Checking Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void SetTimeout_RequiresCallback(ExecutionMode mode)
    {
        // setTimeout without callback should fail type checking
        var source = @"
            setTimeout();
        ";
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("setTimeout", ex.Message);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void SetTimeout_CallbackMustBeFunction(ExecutionMode mode)
    {
        // setTimeout with non-function callback should fail type checking
        var source = @"
            setTimeout('not a function', 100);
        ";
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("function", ex.Message.ToLower());
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void SetInterval_ExecutesMultipleTimes_Interpreted(ExecutionMode mode)
    {
        // setInterval should execute multiple times (interpreted: count variable)
        // Note: Uses generous timing margins to account for thread scheduling differences
        var source = @"
            let count = 0;
            let t = setInterval(() => { count++; }, 20);
            let start = Date.now();
            while (Date.now() - start < 200) { }
            clearInterval(t);
            console.log(count >= 3);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void SetInterval_ExecutesCallback_Compiled(ExecutionMode mode)
    {
        // setInterval should execute callback (compiled: console.log and self-clear)
        var source = @"
            let t = setInterval(() => {
                console.log('tick');
                clearInterval(t);
                console.log('done');
            }, 20);
            // Keep process alive long enough for callback to execute
            let start = Date.now();
            while (Date.now() - start < 500) { }
            console.log('timeout');
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("tick", output);
        Assert.Contains("done", output);
        // Verify 'done' comes after 'tick' (callback completed properly)
        Assert.True(output.IndexOf("tick") < output.IndexOf("done"),
            "Expected 'done' to appear after 'tick' in output");
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void SetInterval_PassesArgsToCallback_Interpreted(ExecutionMode mode)
    {
        // Additional args should be passed to callback (interpreted: captured variable)
        var source = @"
            let result: any = '';
            let t = setInterval((a: any, b: any) => { result = a + b; }, 10, 'hello', 'world');
            let start = Date.now();
            while (Date.now() - start < 50) { }
            clearInterval(t);
            console.log(result);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("helloworld\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void SetInterval_PassesArgsToCallback_Compiled(ExecutionMode mode)
    {
        // Additional args should be passed to callback (compiled: console.log)
        // Note: Uses generous timing margins to account for thread scheduling differences
        var source = @"
            let t = setInterval((a: any, b: any) => { console.log(a + b); }, 10, 'hello', 'world');
            let start = Date.now();
            while (Date.now() - start < 150) { }
            clearInterval(t);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("helloworld", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void SetInterval_DefaultDelay_Interpreted(ExecutionMode mode)
    {
        // setInterval without delay should default to 0 (interpreted)
        var source = @"
            let executed = false;
            let t = setInterval(() => { executed = true; });
            let start = Date.now();
            while (Date.now() - start < 50) { }
            clearInterval(t);
            console.log(executed);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void SetInterval_DefaultDelay_Compiled(ExecutionMode mode)
    {
        // setInterval without delay should default to 0 and execute (compiled)
        var source = @"
            let t = setInterval(() => { console.log('executed'); });
            let start = Date.now();
            while (Date.now() - start < 50) { }
            clearInterval(t);
            console.log('done');
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("executed", output);
        Assert.Contains("done", output);
    }

    #endregion

    #region clearInterval Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void SetInterval_RequiresCallback(ExecutionMode mode)
    {
        // setInterval without callback should fail type checking
        var source = @"
            setInterval();
        ";
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("setInterval", ex.Message);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void SetInterval_CallbackMustBeFunction(ExecutionMode mode)
    {
        // setInterval with non-function callback should fail type checking
        var source = @"
            setInterval('not a function', 100);
        ";
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("function", ex.Message.ToLower());
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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
}
