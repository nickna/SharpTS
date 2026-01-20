using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests for static blocks (static { ... }) in classes for the IL compiler.
/// Static blocks execute once when a class is initialized, in declaration order with static fields.
/// </summary>
public class StaticBlockTests
{
    // ============== BASIC FUNCTIONALITY ==============

    [Fact]
    public void StaticBlock_ExecutesOnClassDeclaration()
    {
        var source = """
            class Foo {
                static { console.log("block executed"); }
            }
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("block executed\n", output);
    }

    [Fact]
    public void StaticBlock_MultipleBlocksExecuteInOrder()
    {
        var source = """
            class Foo {
                static { console.log("first"); }
                static { console.log("second"); }
                static { console.log("third"); }
            }
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("first\nsecond\nthird\n", output);
    }

    [Fact]
    public void StaticBlock_CanAccessStaticField()
    {
        var source = """
            class Foo {
                static x: number = 42;
                static { console.log(Foo.x); }
            }
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void StaticBlock_CanModifyStaticField()
    {
        var source = """
            class Foo {
                static x: number = 1;
                static { Foo.x = 100; }
            }
            console.log(Foo.x);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("100\n", output);
    }

    [Fact]
    public void StaticBlock_InterleavedWithFieldsInOrder()
    {
        var source = """
            class Foo {
                static a: number = 1;
                static { console.log("block1 a=" + Foo.a); }
                static b: number = Foo.a + 10;
                static { console.log("block2 b=" + Foo.b); }
            }
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("block1 a=1\nblock2 b=11\n", output);
    }

    // ============== THIS BINDING ==============

    [Fact]
    public void StaticBlock_ThisRefersToClass()
    {
        var source = """
            class Foo {
                static value: number = 5;
                static { console.log(this.value); }
            }
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("5\n", output);
    }

    [Fact]
    public void StaticBlock_ThisCanModifyStaticProperty()
    {
        var source = """
            class Foo {
                static value: number = 0;
                static { this.value = 42; }
            }
            console.log(Foo.value);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void StaticBlock_CanCallStaticMethod()
    {
        // Note: Use class name directly for method calls; `this.method()` in static blocks
        // requires runtime dispatch that isn't fully supported in compiled mode yet
        var source = """
            class Foo {
                static greet(): string { return "hello"; }
                static { console.log(Foo.greet()); }
            }
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("hello\n", output);
    }

    // ============== INHERITANCE ==============

    [Fact]
    public void StaticBlock_SubclassBlockRunsAfterSuperclass()
    {
        var source = """
            class Parent {
                static { console.log("parent"); }
            }
            class Child extends Parent {
                static { console.log("child"); }
            }
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("parent\nchild\n", output);
    }

    [Fact]
    public void StaticBlock_CanAccessInheritedStaticField()
    {
        var source = """
            class Parent {
                static value: number = 42;
            }
            class Child extends Parent {
                static { console.log(Parent.value); }
            }
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    // ============== CONTROL FLOW ==============

    [Fact]
    public void StaticBlock_WithIfElse()
    {
        var source = """
            class Foo {
                static value: number = 0;
                static {
                    if (true) {
                        this.value = 1;
                    } else {
                        this.value = 2;
                    }
                }
            }
            console.log(Foo.value);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n", output);
    }

    [Fact]
    public void StaticBlock_WithForLoop()
    {
        var source = """
            class Foo {
                static sum: number = 0;
                static {
                    for (let i = 1; i <= 5; i++) {
                        this.sum = this.sum + i;
                    }
                }
            }
            console.log(Foo.sum);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("15\n", output);
    }

    [Fact]
    public void StaticBlock_BreakInLoop()
    {
        var source = """
            class Foo {
                static count: number = 0;
                static {
                    for (let i = 0; i < 10; i++) {
                        if (i === 3) break;
                        this.count = this.count + 1;
                    }
                }
            }
            console.log(Foo.count);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n", output);
    }

    // ============== EDGE CASES ==============

    [Fact]
    public void StaticBlock_Empty()
    {
        var source = """
            class Foo {
                static { }
            }
            console.log("ok");
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("ok\n", output);
    }

    [Fact(Skip = "Generic classes with static blocks have IL generation issues")]
    public void StaticBlock_InGenericClass()
    {
        var source = """
            class Box<T> {
                static count: number = 0;
                static { this.count = 42; }
            }
            console.log(Box.count);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void StaticBlock_InAbstractClass()
    {
        var source = """
            abstract class Base {
                static value: number = 0;
                static { this.value = 100; }
            }
            console.log(Base.value);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("100\n", output);
    }
}
