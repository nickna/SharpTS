using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// String coercion of a class instance must go through the instance's toString
/// (resolved via its class chain), matching Node and the interpreter (#931).
/// A plain class with no toString brands as "[object Object]"; a user toString
/// override wins. Covers String(x), template literals, and "" + x in both modes.
/// </summary>
public class ClassToStringCoercionTests
{
    [Theory, ModeData]
    public void PlainClass_StringCoercion_BrandsAsObjectObject(ExecutionMode mode)
    {
        // No toString override → Object.prototype brand "[object Object]" (Node),
        // not the bare class name / "<Class> instance" CLR form.
        var source = @"
            class Foo {}
            let f: any = new Foo();
            console.log(String(f));
            console.log(`${f}`);
            console.log('' + f);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("[object Object]\n[object Object]\n[object Object]\n", output);
    }

    [Theory, ModeData]
    public void UserToString_StringCoercion_InvokesOverride(ExecutionMode mode)
    {
        var source = @"
            class Bar { toString() { return 'bar!'; } }
            let z: any = new Bar();
            console.log(String(z));
            console.log(`${z}`);
            console.log('v=' + z);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("bar!\nbar!\nv=bar!\n", output);
    }

    [Theory, ModeData]
    public void UserToString_ReturningNumber_CoercesToStringForm(ExecutionMode mode)
    {
        // toString returning a primitive number is a valid OrdinaryToPrimitive
        // result; it stringifies to the number's natural form.
        var source = @"
            class Num { toString() { return 42; } }
            let n: any = new Num();
            console.log(String(n));
            console.log(`${n}`);
            console.log('' + n);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n42\n42\n", output);
    }
}
