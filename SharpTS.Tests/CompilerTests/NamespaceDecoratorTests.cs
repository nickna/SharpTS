using SharpTS.TypeSystem.Exceptions;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests for the @Namespace decorator which allows TypeScript files to
/// specify the .NET namespace for compiled types.
/// </summary>
public class NamespaceDecoratorTests
{
    [Fact]
    public void Namespace_SingleClass_CompilesAndRuns()
    {
        var source = """
            @Namespace("MyCompany.Libraries")
            class Person {
                name: string;
                constructor(name: string) {
                    this.name = name;
                }
                greet(): string {
                    return "Hello, " + this.name;
                }
            }
            let p = new Person("Alice");
            console.log(p.greet());
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("Hello, Alice\n", output);
    }

    [Fact]
    public void Namespace_MultipleClasses_AllInSameNamespace()
    {
        var source = """
            @Namespace("MyCompany.Models")
            class Point {
                x: number;
                y: number;
                constructor(x: number, y: number) {
                    this.x = x;
                    this.y = y;
                }
            }
            class Rectangle {
                topLeft: Point;
                bottomRight: Point;
                constructor(x1: number, y1: number, x2: number, y2: number) {
                    this.topLeft = new Point(x1, y1);
                    this.bottomRight = new Point(x2, y2);
                }
                width(): number {
                    return this.bottomRight.x - this.topLeft.x;
                }
            }
            let rect = new Rectangle(0, 0, 10, 5);
            console.log(rect.width());
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("10\n", output);
    }

    [Fact]
    public void Namespace_NestedNamespace_Works()
    {
        var source = """
            @Namespace("MyCompany.Libraries.Data")
            class DataPoint {
                value: number;
                constructor(value: number) {
                    this.value = value;
                }
            }
            let d = new DataPoint(42);
            console.log(d.value);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void Namespace_WithStaticMembers_Works()
    {
        var source = """
            @Namespace("Utils")
            class Counter {
                static count: number = 0;
                static increment(): number {
                    Counter.count = Counter.count + 1;
                    return Counter.count;
                }
            }
            console.log(Counter.increment());
            console.log(Counter.increment());
            console.log(Counter.count);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("1\n2\n2\n", output);
    }

    [Fact]
    public void Namespace_WithMethods_Works()
    {
        var source = """
            @Namespace("Math.Geometry")
            class Circle {
                radius: number;
                constructor(radius: number) {
                    this.radius = radius;
                }
                area(): number {
                    return 3.14159 * this.radius * this.radius;
                }
                circumference(): number {
                    return 2 * 3.14159 * this.radius;
                }
            }
            let c = new Circle(5);
            console.log(c.area());
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Contains("78.53", output); // ~78.53975
    }

    [Fact]
    public void Namespace_TypeCheckerValidation_RequiresStringArgument()
    {
        var source = """
            @Namespace(123)
            class Test {}
            """;

        var ex = Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunCompiled(source, DecoratorMode.Legacy));
        Assert.Contains("@Namespace argument must be a string literal", ex.Message);
    }

    [Fact]
    public void Namespace_TypeCheckerValidation_RequiresExactlyOneArgument()
    {
        var source = """
            @Namespace()
            class Test {}
            """;

        var ex = Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunCompiled(source, DecoratorMode.Legacy));
        Assert.Contains("@Namespace requires exactly one string argument", ex.Message);
    }

    [Fact]
    public void NoNamespace_DefaultBehavior_Works()
    {
        // Verify that code without @Namespace still works (backward compatibility)
        var source = """
            class Simple {
                value: number;
                constructor(v: number) {
                    this.value = v;
                }
            }
            let s = new Simple(100);
            console.log(s.value);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("100\n", output);
    }
}
