using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Locks the behaviors tracked by issue #105 across both modes:
/// (1) built-in prototype methods expose ECMA-262 §17 <c>length</c> (the formal
/// parameter count), and (2) RegExp prototype methods validate their receiver's
/// internal slot and throw <c>TypeError</c> when called on a non-RegExp <c>this</c>.
/// These are pinned in the Test262 baseline (RegExp/prototype/exec/S15.10.6.2_A11,
/// _A2_T1, …) but Test262 is a separate project, so cover them here for fast signal.
/// </summary>
public class BuiltInMethodLengthAndReceiverTests
{
    [Theory, ModeData]
    public void RegExpPrototypeMethods_ExposeLength(ExecutionMode mode)
    {
        var source = """
            console.log(RegExp.prototype.exec.length);
            console.log(RegExp.prototype.test.length);
            console.log(RegExp.prototype.exec.hasOwnProperty("length"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n1\ntrue\n", output);
    }

    [Theory, ModeData]
    public void CrossCuttingBuiltInMethods_ExposeLength(ExecutionMode mode)
    {
        // §17: each built-in function's `length` is its formal parameter count.
        var source = """
            console.log(String.prototype.charAt.length);
            console.log(String.prototype.slice.length);
            console.log(Array.prototype.push.length);
            console.log(Array.prototype.indexOf.length);
            console.log(Function.prototype.call.length);
            """;

        var output = TestHarness.Run(source, mode);
        // charAt(pos)=1, slice(start,end)=2, push(...items)=1, indexOf(searchElement,fromIndex?)=1, call(thisArg,...)=1
        Assert.Equal("1\n2\n1\n1\n1\n", output);
    }

    [Theory, ModeData]
    public void RegExpExec_OnNonRegExpReceiver_ThrowsTypeError(ExecutionMode mode)
    {
        // §22.2.6.x: exec/test require `this` to have the [[RegExpMatcher]] slot;
        // a borrowed call on a plain object must throw TypeError.
        var source = """
            function check(label: string, fn: () => void) {
                try { fn(); console.log(label, "no throw"); }
                catch (e: any) { console.log(label, e instanceof TypeError); }
            }
            check("exec", () => RegExp.prototype.exec.call({}, "x"));
            check("test", () => RegExp.prototype.test.call({}, "x"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("exec true\ntest true\n", output);
    }
}
