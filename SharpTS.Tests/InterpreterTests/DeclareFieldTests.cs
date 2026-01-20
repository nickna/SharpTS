using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Interpreter tests for the declare field modifier.
/// Verifies runtime behavior: fields exist but have no initialization.
/// </summary>
public class DeclareFieldTests
{
    [Fact]
    public void DeclareField_Instance_ReturnsNull()
    {
        var source = """
            class Model {
                declare id: number;
            }
            const m = new Model();
            console.log(m.id);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("null\n", output);
    }

    [Fact]
    public void DeclareField_Instance_CanBeSetExternally()
    {
        var source = """
            class Model {
                declare id: number;
            }
            const m = new Model();
            m.id = 42;
            console.log(m.id);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void DeclareField_Static_ReturnsNull()
    {
        var source = """
            class Model {
                declare static version: string;
            }
            console.log(Model.version);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("null\n", output);
    }

    [Fact]
    public void DeclareField_Static_CanBeSetExternally()
    {
        var source = """
            class Model {
                declare static version: string;
            }
            Model.version = "1.0.0";
            console.log(Model.version);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1.0.0\n", output);
    }

    [Fact]
    public void DeclareField_MixedWithRegularFields()
    {
        var source = """
            class Model {
                declare id: number;
                name: string = "default";
            }
            const m = new Model();
            console.log(m.id);
            console.log(m.name);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("null\ndefault\n", output);
    }

    [Fact]
    public void DeclareField_MultipleInstances_IndependentValues()
    {
        var source = """
            class Model {
                declare id: number;
            }
            const m1 = new Model();
            const m2 = new Model();
            m1.id = 1;
            m2.id = 2;
            console.log(m1.id);
            console.log(m2.id);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n", output);
    }

    [Fact]
    public void DeclareField_WithReadonly_AccessUninitialized()
    {
        // Readonly declare fields exist but can only be read (not assigned after construction)
        var source = """
            class Model {
                declare readonly name: string;
            }
            const m = new Model();
            console.log(m.name);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("null\n", output);
    }

    [Fact]
    public void DeclareField_WithPrivateAccess()
    {
        var source = """
            class Model {
                declare private _internal: number;

                setInternal(value: number): void {
                    this._internal = value;
                }

                getInternal(): number {
                    return this._internal;
                }
            }
            const m = new Model();
            console.log(m.getInternal());
            m.setInternal(42);
            console.log(m.getInternal());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("null\n42\n", output);
    }

    [Fact]
    public void DeclareField_StaticWithOtherStaticFields()
    {
        var source = """
            class Model {
                declare static version: string;
                static count: number = 0;
            }
            console.log(Model.version);
            console.log(Model.count);
            Model.version = "1.0";
            Model.count = 5;
            console.log(Model.version);
            console.log(Model.count);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("null\n0\n1.0\n5\n", output);
    }

    [Fact]
    public void DeclareField_ClassExpression()
    {
        var source = """
            const Model = class {
                declare id: number;
                name: string = "default";
            };
            const m = new Model();
            console.log(m.id);
            console.log(m.name);
            m.id = 123;
            console.log(m.id);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("null\ndefault\n123\n", output);
    }
}
