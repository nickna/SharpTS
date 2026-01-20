using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Tests for constructor signatures in interfaces and new on expressions.
/// </summary>
public class ConstructorSignatureTests
{
    [Fact]
    public void ConstructorSignature_BasicInterface()
    {
        var code = """
            interface Point {
                x: number;
                y: number;
            }

            interface PointConstructor {
                new (x: number, y: number): Point;
            }

            class PointImpl implements Point {
                x: number;
                y: number;
                constructor(x: number, y: number) {
                    this.x = x;
                    this.y = y;
                }
            }

            function createPoint(ctor: PointConstructor, x: number, y: number): Point {
                return new ctor(x, y);
            }

            let p = createPoint(PointImpl, 10, 20);
            console.log(p.x);
            console.log(p.y);
            """;

        var result = TestHarness.RunInterpreted(code);
        Assert.Equal("10\n20\n", result);
    }

    [Fact]
    public void NewOnVariable_SimpleClass()
    {
        var code = """
            class Box {
                value: number;
                constructor(v: number) {
                    this.value = v;
                }
            }

            let C = Box;
            let b = new C(42);
            console.log(b.value);
            """;

        var result = TestHarness.RunInterpreted(code);
        Assert.Equal("42\n", result);
    }

    [Fact]
    public void NewOnVariable_FunctionReturnsInstance()
    {
        // Test calling new on a class stored in a variable through a function
        var code = """
            class Widget {
                label: string;
                constructor(label: string) {
                    this.label = label;
                }
            }

            function makeWidget(label: string): Widget {
                return new Widget(label);
            }

            let w = makeWidget("test");
            console.log(w.label);
            """;

        var result = TestHarness.RunInterpreted(code);
        Assert.Equal("test\n", result);
    }

    [Fact]
    public void ConstructorSignature_WithOptionalParam()
    {
        // Test constructor signature with optional parameter
        var code = """
            interface Widget {
                label: string;
            }

            interface WidgetConstructor {
                new (label?: string): Widget;
            }

            class DefaultWidget implements Widget {
                label: string;
                constructor(label: string = "default") {
                    this.label = label;
                }
            }

            function createWidget(ctor: WidgetConstructor): Widget {
                return new ctor("custom");
            }

            let w = createWidget(DefaultWidget);
            console.log(w.label);
            """;

        var result = TestHarness.RunInterpreted(code);
        Assert.Equal("custom\n", result);
    }

    [Fact]
    public void NewOnNamespacedClass()
    {
        var code = """
            namespace Geometry {
                export class Point {
                    x: number;
                    y: number;
                    constructor(x: number, y: number) {
                        this.x = x;
                        this.y = y;
                    }
                }
            }

            let p = new Geometry.Point(5, 10);
            console.log(p.x);
            console.log(p.y);
            """;

        var result = TestHarness.RunInterpreted(code);
        Assert.Equal("5\n10\n", result);
    }
}
