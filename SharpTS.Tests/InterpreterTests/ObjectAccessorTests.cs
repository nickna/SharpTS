using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class ObjectAccessorTests
{
    [Fact]
    public void Getter_ReturnsValue()
    {
        var source = """
            let obj = {
                _value: 42,
                get value(): number {
                    return this._value;
                }
            };
            console.log(obj.value);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void Setter_SetsValue()
    {
        var source = """
            let obj = {
                _value: 0,
                get value(): number {
                    return this._value;
                },
                set value(v: number) {
                    this._value = v;
                }
            };
            obj.value = 100;
            console.log(obj.value);
            console.log(obj._value);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("100\n100\n", output);
    }

    [Fact]
    public void Getter_BindsThis()
    {
        var source = """
            let obj = {
                name: "test",
                get greeting(): string {
                    return "Hello, " + this.name;
                }
            };
            console.log(obj.greeting);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Hello, test\n", output);
    }

    [Fact]
    public void Setter_BindsThis()
    {
        var source = """
            let obj = {
                _firstName: "",
                _lastName: "",
                set fullName(name: string) {
                    let parts = name.split(" ");
                    this._firstName = parts[0];
                    this._lastName = parts[1];
                }
            };
            obj.fullName = "John Doe";
            console.log(obj._firstName);
            console.log(obj._lastName);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("John\nDoe\n", output);
    }

    [Fact]
    public void Getter_ComputedValue()
    {
        var source = """
            let obj = {
                _multiplier: 3,
                get tripled(): number {
                    return this._multiplier * 5;
                }
            };
            console.log(obj.tripled);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("15\n", output);
    }

    [Fact]
    public void GetterWithoutSetter_IsReadOnly()
    {
        var source = """
            let obj = {
                _value: 10,
                get value(): number {
                    return this._value;
                }
            };
            console.log(obj.value);
            // Setting won't work but shouldn't throw in non-strict
            obj.value = 20;
            console.log(obj.value);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n10\n", output);
    }

    [Fact]
    public void MixedAccessorsAndRegularProperties()
    {
        var source = """
            let obj = {
                regularProp: "regular",
                _hidden: 0,
                get accessorProp(): number {
                    return this._hidden * 2;
                },
                set accessorProp(v: number) {
                    this._hidden = v;
                }
            };
            console.log(obj.regularProp);
            obj.accessorProp = 5;
            console.log(obj.accessorProp);
            console.log(obj._hidden);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("regular\n10\n5\n", output);
    }

    [Fact]
    public void Getter_WithMethod()
    {
        var source = """
            let obj = {
                count: 0,
                get doubled(): number {
                    return this.count * 2;
                },
                increment(): void {
                    this.count++;
                }
            };
            console.log(obj.doubled);
            obj.increment();
            obj.increment();
            console.log(obj.doubled);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n4\n", output);
    }

    [Fact]
    public void MultipleGetters()
    {
        var source = """
            let obj = {
                _a: 1,
                _b: 2,
                get a(): number {
                    return this._a;
                },
                get b(): number {
                    return this._b;
                }
            };
            console.log(obj.a);
            console.log(obj.b);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n", output);
    }

    [Fact]
    public void SpreadWithGetter_MergesProperties()
    {
        var source = """
            let base = { a: 1, b: 2 };
            let obj = {
                ...base,
                _val: 10,
                get doubled(): number {
                    return this._val * 2;
                }
            };
            console.log(obj.a);
            console.log(obj.b);
            console.log(obj.doubled);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n20\n", output);
    }

    [Fact]
    public void SpreadWithGetterAndSetter_MergesProperties()
    {
        var source = """
            let base = { x: 100, y: 200 };
            let obj = {
                ...base,
                _offset: 10,
                get adjusted(): number {
                    return this.x + this._offset;
                },
                set offset(v: number) {
                    this._offset = v;
                }
            };
            console.log(obj.x);
            console.log(obj.y);
            console.log(obj.adjusted);
            obj.offset = 50;
            console.log(obj.adjusted);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("100\n200\n110\n150\n", output);
    }

    [Fact]
    public void SpreadFromObjectWithAccessor_CopiesDataProperties()
    {
        var source = """
            let source = {
                data: 42,
                get computed(): number {
                    return this.data * 2;
                }
            };
            let target = {
                ...source,
                get tripled(): number {
                    return this.data * 3;
                }
            };
            console.log(target.data);
            console.log(target.tripled);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n126\n", output);
    }
}
