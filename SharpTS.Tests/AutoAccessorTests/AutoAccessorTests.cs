using Xunit;
using SharpTS.Tests.Infrastructure;

namespace SharpTS.Tests.AutoAccessorTests;

/// <summary>
/// Tests for TypeScript 4.9+ auto-accessor class fields (accessor keyword).
/// </summary>
public class AutoAccessorTests
{
    #region Basic Auto-Accessor Tests

    [Fact]
    public void AutoAccessor_BasicGetSet()
    {
        var code = @"
            class Point {
                accessor x: number = 0;
            }
            let p = new Point();
            p.x = 10;
            console.log(p.x);
        ";
        var result = TestHarness.RunInterpreted(code);
        Assert.Equal("10\n", result);
    }

    [Fact]
    public void AutoAccessor_MultipleProperties()
    {
        var code = @"
            class Point {
                accessor x: number = 0;
                accessor y: number = 0;
            }
            let p = new Point();
            p.x = 5;
            p.y = 10;
            console.log(p.x + p.y);
        ";
        var result = TestHarness.RunInterpreted(code);
        Assert.Equal("15\n", result);
    }

    [Fact]
    public void AutoAccessor_WithStringType()
    {
        var code = @"
            class Person {
                accessor name: string = ""unknown"";
            }
            let p = new Person();
            p.name = ""Alice"";
            console.log(p.name);
        ";
        var result = TestHarness.RunInterpreted(code);
        Assert.Equal("Alice\n", result);
    }

    [Fact]
    public void AutoAccessor_WithInitializer()
    {
        var code = @"
            class Counter {
                accessor value: number = 42;
            }
            let c = new Counter();
            console.log(c.value);
        ";
        var result = TestHarness.RunInterpreted(code);
        Assert.Equal("42\n", result);
    }

    [Fact]
    public void AutoAccessor_WithoutInitializer()
    {
        var code = @"
            class Container {
                accessor data: any;
            }
            let c = new Container();
            console.log(c.data === null || c.data === undefined);
        ";
        var result = TestHarness.RunInterpreted(code);
        Assert.Equal("true\n", result);
    }

    #endregion

    #region Static Auto-Accessor Tests

    [Fact]
    public void AutoAccessor_Static_BasicGetSet()
    {
        var code = @"
            class Counter {
                static accessor count: number = 0;
            }
            Counter.count = 5;
            console.log(Counter.count);
        ";
        var result = TestHarness.RunInterpreted(code);
        Assert.Equal("5\n", result);
    }

    [Fact]
    public void AutoAccessor_Static_SharedAcrossInstances()
    {
        var code = @"
            class Counter {
                static accessor total: number = 0;
                constructor() {
                    Counter.total = Counter.total + 1;
                }
            }
            new Counter();
            new Counter();
            new Counter();
            console.log(Counter.total);
        ";
        var result = TestHarness.RunInterpreted(code);
        Assert.Equal("3\n", result);
    }

    [Fact]
    public void AutoAccessor_Static_WithInitializer()
    {
        var code = @"
            class Config {
                static accessor version: string = ""1.0.0"";
            }
            console.log(Config.version);
        ";
        var result = TestHarness.RunInterpreted(code);
        Assert.Equal("1.0.0\n", result);
    }

    #endregion

    #region Readonly Auto-Accessor Tests

    [Fact]
    public void AutoAccessor_Readonly_CanRead()
    {
        var code = @"
            class Immutable {
                readonly accessor id: string = ""abc123"";
            }
            let i = new Immutable();
            console.log(i.id);
        ";
        var result = TestHarness.RunInterpreted(code);
        Assert.Equal("abc123\n", result);
    }

    [Fact]
    public void AutoAccessor_Readonly_ThrowsOnSet()
    {
        var code = @"
            class Immutable {
                readonly accessor id: string = ""abc123"";
            }
            let i = new Immutable();
            i.id = ""new"";
        ";
        // Type checker catches this and reports "Cannot assign to 'id' because it has no setter"
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(code));
        Assert.Contains("Cannot assign", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Inheritance Tests

    [Fact]
    public void AutoAccessor_Inheritance_InheritedFromParent()
    {
        var code = @"
            class Animal {
                accessor name: string = ""unknown"";
            }
            class Dog extends Animal {
            }
            let d = new Dog();
            d.name = ""Rex"";
            console.log(d.name);
        ";
        var result = TestHarness.RunInterpreted(code);
        Assert.Equal("Rex\n", result);
    }

    [Fact]
    public void AutoAccessor_Inheritance_Override()
    {
        var code = @"
            class Base {
                accessor value: number = 10;
            }
            class Derived extends Base {
                override accessor value: number = 20;
            }
            let d = new Derived();
            console.log(d.value);
        ";
        var result = TestHarness.RunInterpreted(code);
        Assert.Equal("20\n", result);
    }

    #endregion

    #region Multiple Instances Tests

    [Fact]
    public void AutoAccessor_MultipleInstances_IsolatedStorage()
    {
        var code = @"
            class Counter {
                accessor count: number = 0;
            }
            let a = new Counter();
            let b = new Counter();
            a.count = 5;
            b.count = 10;
            console.log(a.count + b.count);
        ";
        var result = TestHarness.RunInterpreted(code);
        Assert.Equal("15\n", result);
    }

    #endregion

    #region Type Inference Tests

    [Fact]
    public void AutoAccessor_TypeInference_FromInitializer()
    {
        // When no explicit type annotation is provided, the type is inferred from initializer.
        // Note: TypeScript infers literal types, so we use a number type here.
        var code = @"
            class Container {
                accessor value: number = 42;
            }
            let c = new Container();
            c.value = 100;
            console.log(c.value);
        ";
        var result = TestHarness.RunInterpreted(code);
        Assert.Equal("100\n", result);
    }

    #endregion

    #region Mixed Member Tests

    [Fact]
    public void AutoAccessor_WithRegularFields()
    {
        var code = @"
            class Mixed {
                regularField: number = 1;
                accessor autoAccessor: number = 2;
            }
            let m = new Mixed();
            console.log(m.regularField + m.autoAccessor);
        ";
        var result = TestHarness.RunInterpreted(code);
        Assert.Equal("3\n", result);
    }

    [Fact]
    public void AutoAccessor_WithMethods()
    {
        var code = @"
            class Counter {
                accessor value: number = 0;

                increment() {
                    this.value = this.value + 1;
                }
            }
            let c = new Counter();
            c.increment();
            c.increment();
            console.log(c.value);
        ";
        var result = TestHarness.RunInterpreted(code);
        Assert.Equal("2\n", result);
    }

    [Fact]
    public void AutoAccessor_WithManualAccessor()
    {
        var code = @"
            class Container {
                accessor auto: number = 10;
                private _manual: number = 20;

                get manual(): number { return this._manual; }
                set manual(v: number) { this._manual = v; }
            }
            let c = new Container();
            console.log(c.auto + c.manual);
        ";
        var result = TestHarness.RunInterpreted(code);
        Assert.Equal("30\n", result);
    }

    #endregion

    #region Compiled Auto-Accessor Tests

    [Fact]
    public void AutoAccessor_Compiled_BasicGetSet()
    {
        var code = @"
            class Point {
                accessor x: number = 0;
            }
            let p = new Point();
            p.x = 10;
            console.log(p.x);
        ";
        var result = TestHarness.RunCompiled(code);
        Assert.Equal("10\n", result);
    }

    [Fact]
    public void AutoAccessor_Compiled_Static_BasicGetSet()
    {
        var code = @"
            class Counter {
                static accessor count: number = 0;
            }
            Counter.count = 5;
            console.log(Counter.count);
        ";
        var result = TestHarness.RunCompiled(code);
        Assert.Equal("5\n", result);
    }

    [Fact]
    public void AutoAccessor_Compiled_Static_SharedAcrossInstances()
    {
        var code = @"
            class Counter {
                static accessor total: number = 0;
                constructor() {
                    Counter.total = Counter.total + 1;
                }
            }
            new Counter();
            new Counter();
            new Counter();
            console.log(Counter.total);
        ";
        var result = TestHarness.RunCompiled(code);
        Assert.Equal("3\n", result);
    }

    [Fact]
    public void AutoAccessor_Compiled_Static_WithInitializer()
    {
        var code = @"
            class Config {
                static accessor version: string = ""1.0.0"";
            }
            console.log(Config.version);
        ";
        var result = TestHarness.RunCompiled(code);
        Assert.Equal("1.0.0\n", result);
    }

    [Fact]
    public void AutoAccessor_Compiled_MixedStaticAndInstance()
    {
        var code = @"
            class Counter {
                static accessor total: number = 0;
                accessor value: number = 0;

                constructor() {
                    Counter.total = Counter.total + 1;
                    this.value = Counter.total;
                }
            }
            let a = new Counter();
            let b = new Counter();
            console.log(a.value);
            console.log(b.value);
            console.log(Counter.total);
        ";
        var result = TestHarness.RunCompiled(code);
        Assert.Equal("1\n2\n2\n", result);
    }

    #endregion
}
