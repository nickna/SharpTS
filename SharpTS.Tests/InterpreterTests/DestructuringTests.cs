using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class DestructuringTests
{
    [Fact]
    public void ArrayDestructuring_BasicAssignment()
    {
        var source = """
            const arr: number[] = [1, 2, 3];
            const [a, b] = arr;
            console.log(a);
            console.log(b);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n", output);
    }

    [Fact]
    public void ArrayDestructuring_WithRest()
    {
        var source = """
            const [head, ...tail] = [1, 2, 3, 4];
            console.log(head);
            console.log(tail.length);
            console.log(tail[0]);
            console.log(tail[1]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n3\n2\n3\n", output);
    }

    [Fact]
    public void ArrayDestructuring_WithHoles()
    {
        var source = """
            const [first, , third] = [1, 2, 3];
            console.log(first);
            console.log(third);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n3\n", output);
    }

    [Fact]
    public void ObjectDestructuring_BasicAssignment()
    {
        var source = """
            const obj = { name: "Alice", age: 30 };
            const { name, age } = obj;
            console.log(name);
            console.log(age);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Alice\n30\n", output);
    }

    [Fact]
    public void ObjectDestructuring_WithRename()
    {
        var source = """
            const obj = { name: "Bob" };
            const { name: userName } = obj;
            console.log(userName);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Bob\n", output);
    }

    [Fact]
    public void ObjectDestructuring_WithDefault()
    {
        var source = """
            const obj: any = {};
            const { missing: value = "default" } = obj;
            console.log(value);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("default\n", output);
    }

    [Fact]
    public void NestedDestructuring_Arrays()
    {
        var source = """
            const nested: number[][] = [[1, 2], [3, 4]];
            const [[a, b], [c, d]] = nested;
            console.log(a);
            console.log(b);
            console.log(c);
            console.log(d);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n3\n4\n", output);
    }

    [Fact]
    public void NestedDestructuring_Objects()
    {
        var source = """
            const user = { profile: { email: "test@test.com" } };
            const { profile: { email } } = user;
            console.log(email);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("test@test.com\n", output);
    }

    [Fact]
    public void MixedDestructuring_ObjectWithArray()
    {
        var source = """
            const data = { items: [1, 2, 3] };
            const { items: [m1, m2] } = data;
            console.log(m1);
            console.log(m2);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n", output);
    }

    [Fact]
    public void ArrayDestructuring_TrailingComma()
    {
        var source = """
            const [t1, t2,] = [100, 200];
            console.log(t1);
            console.log(t2);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("100\n200\n", output);
    }
}
