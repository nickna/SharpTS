using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for static blocks (static { ... }) in classes.
/// Static blocks execute once when a class is initialized, in declaration order with static fields.
/// Runs against both interpreter and compiler.
/// </summary>
public class StaticBlockTests
{
    // ============== BASIC FUNCTIONALITY ==============

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticBlock_ExecutesOnClassDeclaration(ExecutionMode mode)
    {
        var source = """
            class Foo {
                static { console.log("block executed"); }
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("block executed\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticBlock_MultipleBlocksExecuteInOrder(ExecutionMode mode)
    {
        var source = """
            class Foo {
                static { console.log("first"); }
                static { console.log("second"); }
                static { console.log("third"); }
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("first\nsecond\nthird\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticBlock_CanAccessStaticField(ExecutionMode mode)
    {
        var source = """
            class Foo {
                static x: number = 42;
                static { console.log(Foo.x); }
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticBlock_CanModifyStaticField(ExecutionMode mode)
    {
        var source = """
            class Foo {
                static x: number = 1;
                static { Foo.x = 100; }
            }
            console.log(Foo.x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticBlock_InterleavedWithFieldsInOrder(ExecutionMode mode)
    {
        var source = """
            class Foo {
                static a: number = 1;
                static { console.log("block1 a=" + Foo.a); }
                static b: number = Foo.a + 10;
                static { console.log("block2 b=" + Foo.b); }
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("block1 a=1\nblock2 b=11\n", output);
    }

    // ============== THIS BINDING ==============

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticBlock_ThisRefersToClass(ExecutionMode mode)
    {
        var source = """
            class Foo {
                static value: number = 5;
                static { console.log(this.value); }
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticBlock_ThisCanModifyStaticProperty(ExecutionMode mode)
    {
        var source = """
            class Foo {
                static value: number = 0;
                static { this.value = 42; }
            }
            console.log(Foo.value);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void StaticBlock_ThisCanCallStaticMethod(ExecutionMode mode)
    {
        // Compiler does not yet support this.method() in static blocks (requires runtime dispatch)
        var source = """
            class Foo {
                static greet(): string { return "hello"; }
                static { console.log(this.greet()); }
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticBlock_CanCallStaticMethod(ExecutionMode mode)
    {
        // Using class name directly works in both interpreter and compiler
        var source = """
            class Foo {
                static greet(): string { return "hello"; }
                static { console.log(Foo.greet()); }
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    // ============== INHERITANCE ==============

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticBlock_SubclassBlockRunsAfterSuperclass(ExecutionMode mode)
    {
        var source = """
            class Parent {
                static { console.log("parent"); }
            }
            class Child extends Parent {
                static { console.log("child"); }
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("parent\nchild\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticBlock_CanAccessInheritedStaticField(ExecutionMode mode)
    {
        var source = """
            class Parent {
                static value: number = 42;
            }
            class Child extends Parent {
                static { console.log(Parent.value); }
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    // ============== CLASS EXPRESSIONS ==============

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void StaticBlock_InAnonymousClassExpression(ExecutionMode mode)
    {
        // Compiler does not yet support static blocks in class expressions
        var source = """
            const Foo = class {
                static value: number = 0;
                static { this.value = 99; }
            };
            console.log(Foo.value);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("99\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void StaticBlock_InNamedClassExpression(ExecutionMode mode)
    {
        // Compiler does not yet support static blocks in class expressions
        var source = """
            const Foo = class Bar {
                static { console.log("named class expr"); }
            };
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("named class expr\n", output);
    }

    // ============== CONTROL FLOW ==============

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticBlock_WithIfElse(ExecutionMode mode)
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
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticBlock_WithForLoop(ExecutionMode mode)
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
        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticBlock_WithTryCatch(ExecutionMode mode)
    {
        var source = """
            class Foo {
                static status: string = "init";
                static {
                    try {
                        throw new Error("test");
                    } catch (e) {
                        this.status = "caught";
                    }
                }
            }
            console.log(Foo.status);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("caught\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticBlock_BreakInLoop(ExecutionMode mode)
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
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    // ============== EDGE CASES ==============

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticBlock_Empty(ExecutionMode mode)
    {
        var source = """
            class Foo {
                static { }
            }
            console.log("ok");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("ok\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticBlock_InGenericClass(ExecutionMode mode)
    {
        var source = """
            class Box<T> {
                static count: number = 0;
                static { this.count = 42; }
            }
            console.log(Box.count);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticBlock_InAbstractClass(ExecutionMode mode)
    {
        var source = """
            abstract class Base {
                static value: number = 0;
                static { this.value = 100; }
            }
            console.log(Base.value);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticBlock_ThrowPropagates(ExecutionMode mode)
    {
        var source = """
            class Foo {
                static { throw new Error("init failed"); }
            }
            """;
        Assert.Throws<Exception>(() => TestHarness.Run(source, mode));
    }

    // ============== ERROR VALIDATION ==============

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticBlock_ReturnStatementNotAllowed(ExecutionMode mode)
    {
        var source = """
            class Foo {
                static { return; }
            }
            """;
        var ex = Assert.Throws<TypeCheckException>(() => TestHarness.Run(source, mode));
        Assert.Contains("Return statements are not allowed in static blocks", ex.Message);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticBlock_ReturnValueNotAllowed(ExecutionMode mode)
    {
        var source = """
            class Foo {
                static { return 42; }
            }
            """;
        var ex = Assert.Throws<TypeCheckException>(() => TestHarness.Run(source, mode));
        Assert.Contains("Return statements are not allowed in static blocks", ex.Message);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticBlock_NoAccessModifiersAllowed(ExecutionMode mode)
    {
        var source = """
            class Foo {
                public static { }
            }
            """;
        var ex = Assert.Throws<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("Static blocks cannot have access modifiers", ex.Message);
    }
}
