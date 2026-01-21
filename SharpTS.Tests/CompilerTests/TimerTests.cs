using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Timer tests for compiled code.
/// setTimeout/clearTimeout are fully implemented including callback execution.
/// NOTE: Tests that modify captured variables may not work due to a pre-existing
/// compiler closure limitation (captured variable mutations don't propagate to outer scope).
/// </summary>
/// <remarks>
/// Timer tests run in a dedicated collection to avoid race conditions with other tests.
/// </remarks>
[Collection("TimerTests")]
public class TimerTests
{
    // ========== setTimeout Basic Tests ==========

    [Fact]
    public void SetTimeout_ReturnsTimeout()
    {
        // setTimeout should return a Timeout object
        var source = @"
            let t = setTimeout(() => {}, 100);
            console.log(typeof t);
            console.log(t.toString().startsWith('Timeout'));
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("object\ntrue\n", output);
    }

    // ========== setTimeout Callback Execution Tests ==========

    [Fact]
    public void SetTimeout_ZeroDelay_ExecutesCallback()
    {
        // setTimeout with 0 delay should execute callback
        var source = @"
            setTimeout(() => { console.log('executed'); }, 0);
            // Busy wait to allow async execution
            let start = Date.now();
            while (Date.now() - start < 100) { }
            console.log('done');
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Contains("executed", output);
        Assert.Contains("done", output);
    }

    [Fact]
    public void SetTimeout_DefaultDelay_ExecutesCallback()
    {
        // setTimeout without delay should default to 0 and execute
        var source = @"
            setTimeout(() => { console.log('executed'); });
            let start = Date.now();
            while (Date.now() - start < 100) { }
            console.log('done');
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Contains("executed", output);
        Assert.Contains("done", output);
    }

    [Fact]
    public void SetTimeout_PassesArgsToCallback()
    {
        // Additional args should be passed to callback
        var source = @"
            setTimeout((a: any, b: any) => { console.log(a + b); }, 0, 'hello', 'world');
            let start = Date.now();
            while (Date.now() - start < 100) { }
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Contains("helloworld", output);
    }

    [Fact]
    public void ClearTimeout_PreventsExecution()
    {
        // clearTimeout should prevent callback from executing
        var source = @"
            let t = setTimeout(() => { console.log('should not run'); }, 100);
            clearTimeout(t);
            // Wait longer than the timeout delay
            let start = Date.now();
            while (Date.now() - start < 200) { }
            console.log('done');
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.DoesNotContain("should not run", output);
        Assert.Contains("done", output);
    }

    // ========== clearTimeout Tests ==========

    [Fact]
    public void ClearTimeout_Null_DoesNotThrow()
    {
        // clearTimeout(null) should not throw
        var source = @"
            clearTimeout(null);
            console.log('ok');
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("ok\n", output);
    }

    [Fact]
    public void ClearTimeout_Undefined_DoesNotThrow()
    {
        // clearTimeout(undefined) should not throw
        var source = @"
            clearTimeout(undefined);
            console.log('ok');
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("ok\n", output);
    }

    [Fact]
    public void ClearTimeout_NoArgs_DoesNotThrow()
    {
        // clearTimeout() with no args should not throw
        var source = @"
            clearTimeout();
            console.log('ok');
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("ok\n", output);
    }

    // ========== ref/unref Tests ==========

    [Fact]
    public void Timeout_Ref_ReturnsSameObject()
    {
        // ref() should return the same Timeout object for chaining
        var source = @"
            let t = setTimeout(() => {}, 100);
            let t2 = t.ref();
            console.log(t === t2);
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Timeout_Unref_ReturnsSameObject()
    {
        // unref() should return the same Timeout object for chaining
        var source = @"
            let t = setTimeout(() => {}, 100);
            let t2 = t.unref();
            console.log(t === t2);
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Timeout_HasRef_DefaultsToTrue()
    {
        // hasRef should default to true
        var source = @"
            let t = setTimeout(() => {}, 100);
            console.log(t.hasRef);
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Timeout_Unref_SetsHasRefFalse()
    {
        // unref() should set hasRef to false
        var source = @"
            let t = setTimeout(() => {}, 100);
            t.unref();
            console.log(t.hasRef);
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("false\n", output);
    }

    [Fact]
    public void Timeout_RefAfterUnref_SetsHasRefTrue()
    {
        // ref() after unref() should set hasRef back to true
        var source = @"
            let t = setTimeout(() => {}, 100);
            t.unref();
            t.ref();
            console.log(t.hasRef);
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Timeout_MethodChaining()
    {
        // ref/unref should support method chaining
        var source = @"
            let t = setTimeout(() => {}, 100).unref().ref();
            console.log(t.hasRef);
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    // ========== Type Checking Tests ==========

    [Fact]
    public void SetTimeout_RequiresCallback()
    {
        // setTimeout without callback should fail type checking
        var source = @"
            setTimeout();
        ";
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunCompiled(source));
        Assert.Contains("setTimeout", ex.Message);
    }

    [Fact]
    public void SetTimeout_CallbackMustBeFunction()
    {
        // setTimeout with non-function callback should fail type checking
        var source = @"
            setTimeout('not a function', 100);
        ";
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunCompiled(source));
        Assert.Contains("function", ex.Message.ToLower());
    }

    [Fact]
    public void SetTimeout_DelayMustBeNumber()
    {
        // setTimeout with non-number delay should fail type checking
        var source = @"
            setTimeout(() => {}, 'not a number');
        ";
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunCompiled(source));
        Assert.Contains("number", ex.Message.ToLower());
    }

    // ========== setInterval Basic Tests ==========

    [Fact]
    public void SetInterval_ReturnsTimeout()
    {
        // setInterval should return a Timeout object (same as setTimeout)
        var source = @"
            let t = setInterval(() => {}, 100);
            console.log(typeof t);
            console.log(t.toString().startsWith('Timeout'));
            clearInterval(t);
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("object\ntrue\n", output);
    }

    [Fact]
    public void SetInterval_ExecutesCallback()
    {
        // setInterval should execute callback
        var source = @"
            let t = setInterval(() => { console.log('tick'); }, 20);
            let start = Date.now();
            while (Date.now() - start < 100) { }
            clearInterval(t);
            console.log('done');
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Contains("tick", output);
        Assert.Contains("done", output);
    }

    [Fact]
    public void ClearInterval_StopsExecution()
    {
        // clearInterval should stop the interval from executing
        var source = @"
            let t = setInterval(() => { console.log('should not appear after clear'); }, 100);
            clearInterval(t);
            let start = Date.now();
            while (Date.now() - start < 200) { }
            console.log('done');
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.DoesNotContain("should not appear after clear", output);
        Assert.Contains("done", output);
    }

    [Fact]
    public void SetInterval_PassesArgsToCallback()
    {
        // Additional args should be passed to callback
        var source = @"
            let t = setInterval((a: any, b: any) => { console.log(a + b); }, 10, 'hello', 'world');
            let start = Date.now();
            while (Date.now() - start < 50) { }
            clearInterval(t);
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Contains("helloworld", output);
    }

    [Fact]
    public void SetInterval_DefaultDelay()
    {
        // setInterval without delay should default to 0 and execute
        var source = @"
            let t = setInterval(() => { console.log('executed'); });
            let start = Date.now();
            while (Date.now() - start < 50) { }
            clearInterval(t);
            console.log('done');
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Contains("executed", output);
        Assert.Contains("done", output);
    }

    // ========== clearInterval Tests ==========

    [Fact]
    public void ClearInterval_Null_DoesNotThrow()
    {
        // clearInterval(null) should not throw
        var source = @"
            clearInterval(null);
            console.log('ok');
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("ok\n", output);
    }

    [Fact]
    public void ClearInterval_Undefined_DoesNotThrow()
    {
        // clearInterval(undefined) should not throw
        var source = @"
            clearInterval(undefined);
            console.log('ok');
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("ok\n", output);
    }

    [Fact]
    public void ClearInterval_NoArgs_DoesNotThrow()
    {
        // clearInterval() with no args should not throw
        var source = @"
            clearInterval();
            console.log('ok');
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("ok\n", output);
    }

    // ========== setInterval ref/unref Tests ==========

    [Fact]
    public void Interval_Ref_ReturnsSameObject()
    {
        // ref() should return the same object for chaining
        var source = @"
            let t = setInterval(() => {}, 100);
            let t2 = t.ref();
            console.log(t === t2);
            clearInterval(t);
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Interval_Unref_ReturnsSameObject()
    {
        // unref() should return the same object for chaining
        var source = @"
            let t = setInterval(() => {}, 100);
            let t2 = t.unref();
            console.log(t === t2);
            clearInterval(t);
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Interval_HasRef_DefaultsToTrue()
    {
        // hasRef should default to true
        var source = @"
            let t = setInterval(() => {}, 100);
            console.log(t.hasRef);
            clearInterval(t);
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    // ========== setInterval Type Checking Tests ==========

    [Fact]
    public void SetInterval_RequiresCallback()
    {
        // setInterval without callback should fail type checking
        var source = @"
            setInterval();
        ";
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunCompiled(source));
        Assert.Contains("setInterval", ex.Message);
    }

    [Fact]
    public void SetInterval_CallbackMustBeFunction()
    {
        // setInterval with non-function callback should fail type checking
        var source = @"
            setInterval('not a function', 100);
        ";
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunCompiled(source));
        Assert.Contains("function", ex.Message.ToLower());
    }

    [Fact]
    public void SetInterval_DelayMustBeNumber()
    {
        // setInterval with non-number delay should fail type checking
        var source = @"
            setInterval(() => {}, 'not a number');
        ";
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunCompiled(source));
        Assert.Contains("number", ex.Message.ToLower());
    }
}
