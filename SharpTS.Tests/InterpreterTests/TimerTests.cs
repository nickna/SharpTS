using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Timer tests run in a dedicated collection to avoid race conditions with other tests.
/// The timer implementation uses async callbacks that can conflict with concurrent test execution.
/// </summary>
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
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("object\ntrue\n", output);
    }

    [Fact]
    public void SetTimeout_ZeroDelay_ExecutesCallback()
    {
        // setTimeout with 0 delay should still execute
        var source = @"
            let executed = false;
            setTimeout(() => { executed = true; }, 0);
            // Small delay to allow async execution
            let start = Date.now();
            while (Date.now() - start < 50) { }
            console.log(executed);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void SetTimeout_DefaultDelay_IsZero()
    {
        // setTimeout without delay should default to 0
        var source = @"
            let executed = false;
            setTimeout(() => { executed = true; });
            let start = Date.now();
            while (Date.now() - start < 50) { }
            console.log(executed);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    // ========== clearTimeout Tests ==========

    [Fact]
    public void ClearTimeout_PreventsExecution()
    {
        // clearTimeout should prevent callback from executing
        var source = @"
            let executed = false;
            let t = setTimeout(() => { executed = true; }, 100);
            clearTimeout(t);
            // Wait longer than the timeout delay
            let start = Date.now();
            while (Date.now() - start < 200) { }
            console.log(executed);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("false\n", output);
    }

    [Fact]
    public void ClearTimeout_Null_DoesNotThrow()
    {
        // clearTimeout(null) should not throw
        var source = @"
            clearTimeout(null);
            console.log('ok');
        ";
        var output = TestHarness.RunInterpreted(source);
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
        var output = TestHarness.RunInterpreted(source);
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
        var output = TestHarness.RunInterpreted(source);
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
        var output = TestHarness.RunInterpreted(source);
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
        var output = TestHarness.RunInterpreted(source);
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
        var output = TestHarness.RunInterpreted(source);
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
        var output = TestHarness.RunInterpreted(source);
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
        var output = TestHarness.RunInterpreted(source);
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
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    // ========== setTimeout with Arguments Tests ==========

    [Fact]
    public void SetTimeout_PassesArgsToCallback()
    {
        // Additional args should be passed to callback
        var source = @"
            let result: any = '';
            setTimeout((a: any, b: any) => { result = a + b; }, 0, 'hello', 'world');
            let start = Date.now();
            while (Date.now() - start < 50) { }
            console.log(result);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("helloworld\n", output);
    }

    // ========== Type Checking Tests ==========

    [Fact]
    public void SetTimeout_RequiresCallback()
    {
        // setTimeout without callback should fail type checking
        var source = @"
            setTimeout();
        ";
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("setTimeout", ex.Message);
    }

    [Fact]
    public void SetTimeout_CallbackMustBeFunction()
    {
        // setTimeout with non-function callback should fail type checking
        var source = @"
            setTimeout('not a function', 100);
        ";
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("function", ex.Message.ToLower());
    }

    [Fact]
    public void SetTimeout_DelayMustBeNumber()
    {
        // setTimeout with non-number delay should fail type checking
        var source = @"
            setTimeout(() => {}, 'not a number');
        ";
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
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
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("object\ntrue\n", output);
    }

    [Fact]
    public void SetInterval_ExecutesMultipleTimes()
    {
        // setInterval should execute multiple times
        // Note: Uses generous timing margins to account for thread scheduling differences
        // across platforms (especially macOS ARM64 where thread pool may be slower)
        var source = @"
            let count = 0;
            let t = setInterval(() => { count++; }, 20);
            let start = Date.now();
            while (Date.now() - start < 200) { }
            clearInterval(t);
            console.log(count >= 3);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void ClearInterval_StopsExecution()
    {
        // clearInterval should stop the interval
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
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void SetInterval_PassesArgsToCallback()
    {
        // Additional args should be passed to callback
        var source = @"
            let result: any = '';
            let t = setInterval((a: any, b: any) => { result = a + b; }, 10, 'hello', 'world');
            let start = Date.now();
            while (Date.now() - start < 50) { }
            clearInterval(t);
            console.log(result);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("helloworld\n", output);
    }

    [Fact]
    public void SetInterval_DefaultDelay()
    {
        // setInterval without delay should default to 0
        var source = @"
            let executed = false;
            let t = setInterval(() => { executed = true; });
            let start = Date.now();
            while (Date.now() - start < 50) { }
            clearInterval(t);
            console.log(executed);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
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
        var output = TestHarness.RunInterpreted(source);
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
        var output = TestHarness.RunInterpreted(source);
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
        var output = TestHarness.RunInterpreted(source);
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
        var output = TestHarness.RunInterpreted(source);
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
        var output = TestHarness.RunInterpreted(source);
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
        var output = TestHarness.RunInterpreted(source);
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
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("setInterval", ex.Message);
    }

    [Fact]
    public void SetInterval_CallbackMustBeFunction()
    {
        // setInterval with non-function callback should fail type checking
        var source = @"
            setInterval('not a function', 100);
        ";
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("function", ex.Message.ToLower());
    }

    [Fact]
    public void SetInterval_DelayMustBeNumber()
    {
        // setInterval with non-number delay should fail type checking
        var source = @"
            setInterval(() => {}, 'not a number');
        ";
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("number", ex.Message.ToLower());
    }
}
