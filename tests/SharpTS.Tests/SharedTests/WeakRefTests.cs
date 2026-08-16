using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for WeakRef functionality. Runs against both interpreter and compiler.
/// </summary>
public class WeakRefTests
{
    [Theory, ModeData]
    public void WeakRef_CreateAndDeref(ExecutionMode mode)
    {
        var source = @"
            let obj = { name: ""hello"" };
            let wr = new WeakRef(obj);
            let target = wr.deref()!;
            console.log(target.name);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory, ModeData]
    public void WeakRef_Typeof(ExecutionMode mode)
    {
        var source = @"
            let obj = { id: 1 };
            let wr = new WeakRef(obj);
            console.log(typeof wr);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\n", output);
    }

    [Theory, ModeData]
    public void WeakRef_DerefWithClassInstance(ExecutionMode mode)
    {
        var source = @"
            class User {
                constructor(public name: string) {}
            }
            let user = new User(""Alice"");
            let wr = new WeakRef(user);
            let target = wr.deref()!;
            console.log(target.name);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice\n", output);
    }

    [Theory, ModeData]
    public void WeakRef_DerefReturnsCorrectValue(ExecutionMode mode)
    {
        var source = @"
            let obj = { x: 10, y: 20 };
            let wr = new WeakRef(obj);
            let target = wr.deref()!;
            console.log(target.x);
            console.log(target.y);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n", output);
    }

    [Theory, ModeData]
    public void WeakRef_WithArrayTarget(ExecutionMode mode)
    {
        var source = @"
            let arr = [1, 2, 3];
            let wr = new WeakRef(arr);
            let target = wr.deref()!;
            console.log(target.length);
            console.log(target[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n1\n", output);
    }

    [Theory, ModeData]
    public void WeakRef_MultipleRefs(ExecutionMode mode)
    {
        var source = @"
            let obj1 = { id: 1 };
            let obj2 = { id: 2 };
            let wr1 = new WeakRef(obj1);
            let wr2 = new WeakRef(obj2);
            console.log(wr1.deref()!.id);
            console.log(wr2.deref()!.id);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    [Theory, ModeData]
    public void WeakRef_WithTypeArgument(ExecutionMode mode)
    {
        var source = @"
            class Item {
                constructor(public value: number) {}
            }
            let item = new Item(42);
            let wr = new WeakRef<Item>(item);
            console.log(wr.deref()!.value);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }
}
