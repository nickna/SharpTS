using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests for WeakMap and WeakSet functionality (compiled).
/// </summary>
public class WeakMapSetTests
{
    // ========== WeakMap Basic Tests ==========

    [Fact]
    public void WeakMap_CreateEmpty()
    {
        var source = @"
            let wm = new WeakMap<object, number>();
            console.log(typeof wm);
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("object\n", output);
    }

    [Fact]
    public void WeakMap_SetAndGet()
    {
        var source = @"
            let wm = new WeakMap<object, string>();
            let key = { id: 1 };
            wm.set(key, ""value1"");
            console.log(wm.get(key));
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("value1\n", output);
    }

    [Fact]
    public void WeakMap_Has()
    {
        var source = @"
            let wm = new WeakMap<object, number>();
            let key1 = { id: 1 };
            let key2 = { id: 2 };
            wm.set(key1, 100);
            console.log(wm.has(key1));
            console.log(wm.has(key2));
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\nfalse\n", output);
    }

    [Fact]
    public void WeakMap_Delete()
    {
        var source = @"
            let wm = new WeakMap<object, number>();
            let key = { id: 1 };
            wm.set(key, 100);
            console.log(wm.has(key));
            console.log(wm.delete(key));
            console.log(wm.has(key));
            console.log(wm.delete(key));
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\nfalse\nfalse\n", output);
    }

    [Fact]
    public void WeakMap_SetReturnsWeakMap()
    {
        var source = @"
            let wm = new WeakMap<object, number>();
            let key = { id: 1 };
            let result = wm.set(key, 100);
            console.log(result === wm);
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void WeakMap_ChainedSet()
    {
        var source = @"
            let wm = new WeakMap<object, number>();
            let k1 = { id: 1 };
            let k2 = { id: 2 };
            let k3 = { id: 3 };
            wm.set(k1, 1).set(k2, 2).set(k3, 3);
            console.log(wm.get(k1));
            console.log(wm.get(k2));
            console.log(wm.get(k3));
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Fact]
    public void WeakMap_GetNonExistentKeyReturnsNull()
    {
        var source = @"
            let wm = new WeakMap<object, number>();
            let key = { id: 1 };
            console.log(wm.get(key) == null);
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void WeakMap_UpdateExistingKey()
    {
        var source = @"
            let wm = new WeakMap<object, number>();
            let key = { id: 1 };
            wm.set(key, 100);
            console.log(wm.get(key));
            wm.set(key, 200);
            console.log(wm.get(key));
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("100\n200\n", output);
    }

    [Fact]
    public void WeakMap_MultipleKeys()
    {
        var source = @"
            let wm = new WeakMap<object, string>();
            let k1 = { name: ""a"" };
            let k2 = { name: ""b"" };
            let k3 = { name: ""c"" };
            wm.set(k1, ""first"");
            wm.set(k2, ""second"");
            wm.set(k3, ""third"");
            console.log(wm.get(k1));
            console.log(wm.get(k2));
            console.log(wm.get(k3));
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("first\nsecond\nthird\n", output);
    }

    // ========== WeakSet Basic Tests ==========

    [Fact]
    public void WeakSet_CreateEmpty()
    {
        var source = @"
            let ws = new WeakSet<object>();
            console.log(typeof ws);
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("object\n", output);
    }

    [Fact]
    public void WeakSet_Add()
    {
        var source = @"
            let ws = new WeakSet<object>();
            let obj = { id: 1 };
            ws.add(obj);
            console.log(ws.has(obj));
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void WeakSet_Has()
    {
        var source = @"
            let ws = new WeakSet<object>();
            let obj1 = { id: 1 };
            let obj2 = { id: 2 };
            ws.add(obj1);
            console.log(ws.has(obj1));
            console.log(ws.has(obj2));
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\nfalse\n", output);
    }

    [Fact]
    public void WeakSet_Delete()
    {
        var source = @"
            let ws = new WeakSet<object>();
            let obj = { id: 1 };
            ws.add(obj);
            console.log(ws.has(obj));
            console.log(ws.delete(obj));
            console.log(ws.has(obj));
            console.log(ws.delete(obj));
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\nfalse\nfalse\n", output);
    }

    [Fact]
    public void WeakSet_AddReturnsWeakSet()
    {
        var source = @"
            let ws = new WeakSet<object>();
            let obj = { id: 1 };
            let result = ws.add(obj);
            console.log(result === ws);
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void WeakSet_ChainedAdd()
    {
        var source = @"
            let ws = new WeakSet<object>();
            let o1 = { id: 1 };
            let o2 = { id: 2 };
            let o3 = { id: 3 };
            ws.add(o1).add(o2).add(o3);
            console.log(ws.has(o1));
            console.log(ws.has(o2));
            console.log(ws.has(o3));
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void WeakSet_AddDuplicate()
    {
        var source = @"
            let ws = new WeakSet<object>();
            let obj = { id: 1 };
            ws.add(obj);
            ws.add(obj);
            console.log(ws.has(obj));
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void WeakSet_MultipleObjects()
    {
        var source = @"
            let ws = new WeakSet<object>();
            let o1 = { name: ""a"" };
            let o2 = { name: ""b"" };
            let o3 = { name: ""c"" };
            ws.add(o1);
            ws.add(o2);
            ws.add(o3);
            console.log(ws.has(o1));
            console.log(ws.has(o2));
            console.log(ws.has(o3));
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    // ========== WeakMap with Class Keys ==========

    [Fact]
    public void WeakMap_WithClassInstanceKeys()
    {
        var source = @"
            class User {
                constructor(public name: string) {}
            }
            let wm = new WeakMap<User, number>();
            let user1 = new User(""Alice"");
            let user2 = new User(""Bob"");
            wm.set(user1, 100);
            wm.set(user2, 200);
            console.log(wm.get(user1));
            console.log(wm.get(user2));
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("100\n200\n", output);
    }

    // ========== WeakSet with Class Instances ==========

    [Fact]
    public void WeakSet_WithClassInstances()
    {
        var source = @"
            class Item {
                constructor(public id: number) {}
            }
            let ws = new WeakSet<Item>();
            let item1 = new Item(1);
            let item2 = new Item(2);
            ws.add(item1);
            console.log(ws.has(item1));
            console.log(ws.has(item2));
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\nfalse\n", output);
    }

    // ========== WeakMap with Array Keys ==========

    [Fact]
    public void WeakMap_WithArrayKeys()
    {
        var source = @"
            let wm = new WeakMap<number[], string>();
            let arr1 = [1, 2, 3];
            let arr2 = [4, 5, 6];
            wm.set(arr1, ""first"");
            wm.set(arr2, ""second"");
            console.log(wm.get(arr1));
            console.log(wm.get(arr2));
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("first\nsecond\n", output);
    }

    // ========== WeakSet with Array Elements ==========

    [Fact]
    public void WeakSet_WithArrayElements()
    {
        var source = @"
            let ws = new WeakSet<number[]>();
            let arr1 = [1, 2, 3];
            let arr2 = [4, 5, 6];
            ws.add(arr1);
            console.log(ws.has(arr1));
            console.log(ws.has(arr2));
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\nfalse\n", output);
    }
}
