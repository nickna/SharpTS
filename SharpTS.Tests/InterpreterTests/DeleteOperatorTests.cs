using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class DeleteOperatorTests
{
    [Fact]
    public void Delete_ObjectProperty_ReturnsTrue()
    {
        var source = """
            let obj: { name?: string } = { name: "test" };
            let result: boolean = delete obj.name;
            console.log(result);
            console.log(obj.name === null || obj.name === undefined);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Delete_ComputedProperty_ReturnsTrue()
    {
        var source = """
            let obj: { [key: string]: any } = { key: "value" };
            let result: boolean = delete obj["key"];
            console.log(result);
            console.log(obj["key"] === null || obj["key"] === undefined);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Delete_ExistingProperty_Works()
    {
        var source = """
            let obj: { foo?: string } = { foo: "bar" };
            console.log(obj.foo);
            let result: boolean = delete obj.foo;
            console.log(result);
            console.log(obj.foo === null || obj.foo === undefined);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("bar\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Delete_FrozenObject_ReturnsFalse()
    {
        var source = """
            let obj = { name: "test" };
            Object.freeze(obj);
            let result: boolean = delete obj.name;
            console.log(result);
            console.log(obj.name);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("false\ntest\n", output);
    }

    [Fact]
    public void Delete_SealedObject_ReturnsFalse()
    {
        var source = """
            let obj = { name: "test" };
            Object.seal(obj);
            let result: boolean = delete obj.name;
            console.log(result);
            console.log(obj.name);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("false\ntest\n", output);
    }

    [Fact]
    public void Delete_MultipleProperties()
    {
        var source = """
            let obj: { a?: number, b?: number, c?: number } = { a: 1, b: 2, c: 3 };
            delete obj.a;
            delete obj.c;
            console.log(obj.a === null || obj.a === undefined);
            console.log(obj.b);
            console.log(obj.c === null || obj.c === undefined);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n2\ntrue\n", output);
    }

    [Fact]
    public void Delete_Expression_EvaluatesOperand()
    {
        var source = """
            let obj: { prop?: string } = { prop: "value" };
            if (delete obj.prop) {
                console.log("deleted");
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("deleted\n", output);
    }
}
