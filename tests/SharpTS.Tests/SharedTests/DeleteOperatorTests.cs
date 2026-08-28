using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for the delete operator. Runs against both interpreter and compiler.
/// </summary>
public class DeleteOperatorTests
{
    [Theory, ModeData]
    public void Delete_ObjectProperty_ReturnsTrue(ExecutionMode mode)
    {
        var source = """
            let obj: { name?: string } = { name: "test" };
            let result: boolean = delete obj.name;
            console.log(result);
            console.log(obj.name === null || obj.name === undefined);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Delete_ComputedProperty_ReturnsTrue(ExecutionMode mode)
    {
        var source = """
            let obj: { [key: string]: any } = { key: "value" };
            let result: boolean = delete obj["key"];
            console.log(result);
            console.log(obj["key"] === null || obj["key"] === undefined);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Delete_ExistingProperty_Works(ExecutionMode mode)
    {
        var source = """
            let obj: { foo?: string } = { foo: "bar" };
            console.log(obj.foo);
            let result: boolean = delete obj.foo;
            console.log(result);
            console.log(obj.foo === null || obj.foo === undefined);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("bar\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Delete_FrozenObject_ReturnsFalse(ExecutionMode mode)
    {
        var source = """
            let obj = { name: "test" };
            Object.freeze(obj);
            let result: boolean = delete obj.name;
            console.log(result);
            console.log(obj.name);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\ntest\n", output);
    }

    [Theory, ModeData]
    public void Delete_SealedObject_ReturnsFalse(ExecutionMode mode)
    {
        var source = """
            let obj = { name: "test" };
            Object.seal(obj);
            let result: boolean = delete obj.name;
            console.log(result);
            console.log(obj.name);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\ntest\n", output);
    }

    [Theory, ModeData]
    public void Delete_SealedArray_UsesCanonicalArrayIndexKeys(ExecutionMode mode)
    {
        var source = """
            const aliases: string[] = ["01", "+1", " 1 "];
            const values: any = ["zero", "one"];
            for (const key of aliases) {
                Object.defineProperty(values, key, {
                    value: key,
                    writable: true,
                    enumerable: true,
                    configurable: true
                });
            }

            console.log(values[1]);
            for (const key of aliases) {
                const descriptor = Object.getOwnPropertyDescriptor(values, key)!;
                console.log(values[key] === key,
                    descriptor.value === key,
                    descriptor.enumerable,
                    descriptor.configurable);
            }

            const sealed: any = ["zero", "one"];
            Object.seal(sealed);
            for (const key of aliases) {
                console.log(
                    Object.getOwnPropertyDescriptor(sealed, key) === undefined,
                    delete sealed[key],
                    sealed[1]);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal(
            "one\n" +
            "true true true true\n" +
            "true true true true\n" +
            "true true true true\n" +
            "true true one\n" +
            "true true one\n" +
            "true true one\n",
            output);
    }

    [Theory, ModeData]
    public void Delete_MultipleProperties(ExecutionMode mode)
    {
        var source = """
            let obj: any = { a: 1, b: 2, c: 3 };
            delete obj.a;
            delete obj.c;
            console.log(obj.a === null || obj.a === undefined);
            console.log(obj.b);
            console.log(obj.c === null || obj.c === undefined);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n2\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Delete_Expression_EvaluatesOperand(ExecutionMode mode)
    {
        var source = """
            let obj: { prop?: string } = { prop: "value" };
            if (delete obj.prop) {
                console.log("deleted");
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("deleted\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void Delete_NonConfigurablePropertyHonorsStrictness(ExecutionMode mode)
    {
        var source = """
            const object: any = {};
            Object.defineProperty(object, "fixed", {
                value: 1,
                configurable: false
            });
            console.log(delete object.fixed);
            console.log(delete object.missing);
            (function() {
                "use strict";
                try {
                    delete object.fixed;
                } catch (error) {
                    console.log(error instanceof TypeError);
                }
            })();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\ntrue\ntrue\n", output);
    }
}
