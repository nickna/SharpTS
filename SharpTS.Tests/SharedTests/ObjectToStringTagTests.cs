using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for #314: anything `typeof` reports as "function" must
/// brand "[object Function]" through Object.prototype.toString (ECMA-262
/// 20.1.3.6 step 7, IsCallable) — in particular built-in constructors held
/// as values, which compiled mode represents as System.Type tokens. lodash's
/// baseGetTag/isFunction classifies by this tag; the old "[object Object]"
/// answer sent built-in constructors down its host-constructor path.
/// </summary>
public class ObjectToStringTagTests
{
    [Theory, ModeData]
    public void BuiltInConstructors_TagAsFunction(ExecutionMode mode)
    {
        var source = """
            var O = Object;
            var D = Date;
            var A = Array;
            console.log(Object.prototype.toString.call(O));
            console.log(Object.prototype.toString.call(D));
            console.log(Object.prototype.toString.call(A));
            console.log(typeof O, typeof D, typeof A);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal(
            "[object Function]\n[object Function]\n[object Function]\nfunction function function\n",
            output);
    }

    [Theory, ModeData]
    public void UserFunctions_TagAsFunction(ExecutionMode mode)
    {
        var source = """
            function decl() {}
            const arrow = () => 0;
            const expr = function () {};
            console.log(Object.prototype.toString.call(decl));
            console.log(Object.prototype.toString.call(arrow));
            console.log(Object.prototype.toString.call(expr));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("[object Function]\n[object Function]\n[object Function]\n", output);
    }

    [Theory, ModeData]
    public void NonFunctions_KeepTheirTags(ExecutionMode mode)
    {
        var source = """
            console.log(Object.prototype.toString.call({}));
            console.log(Object.prototype.toString.call([]));
            console.log(Object.prototype.toString.call("s"));
            console.log(Object.prototype.toString.call(1));
            console.log(Object.prototype.toString.call(true));
            console.log(Object.prototype.toString.call(null));
            console.log(Object.prototype.toString.call(undefined));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal(
            "[object Object]\n[object Array]\n[object String]\n[object Number]\n[object Boolean]\n[object Null]\n[object Undefined]\n",
            output);
    }

    [Theory, ModeData]
    public void ObjectKeys_OnFunction_ReturnsExpandoProps(ExecutionMode mode)
    {
        // ECMA-262: Object.keys does ToObject — functions are objects, and
        // their own enumerable keys are the user-assigned expando properties.
        // lodash's keys(lodashFn) hits this once isFunction reports correctly
        // (#314 follow-on fix).
        var source = """
            function f() {}
            console.log(Object.keys(f).length);
            (f as any).alpha = 1;
            (f as any).beta = 2;
            console.log(Object.keys(f).join(","));
            console.log(Object.values(f).join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\nalpha,beta\n1,2\n", output);
    }
}
