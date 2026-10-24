using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ForwardGenericClassTypeTests
{
    [Theory, ModeData]
    public void EarlierAnnotationSelectsLocalGenericClass(ExecutionMode mode)
    {
        const string source = """
            interface Box<T> { outer: T; }
            function inspect() {
                let value: Box<number> | null = null;
                class Box<T> { inner: T | null = null; }
                value = new Box<number>();
                return value.inner;
            }
            console.log(inspect());
            """;
        Assert.Equal("null\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void EarlierHolderUsesTheLocalGenericClass(ExecutionMode mode)
    {
        const string source = """
            interface Box<T> { outer: T; }
            function inspect() {
                class Holder {
                    value: Box<number> | null = null;
                    create() { this.value = new Box<number>(); }
                }
                class Box<T> { inner: T | null = null; }
                const holder = new Holder();
                holder.create();
                return holder.value!.inner;
            }
            console.log(inspect());
            """;
        Assert.Equal("null\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [InlineData("let value: Box<string>; class Box<T extends number> { inner: T; }")]
    [InlineData("let value: Box<number> | null = null; class Box<T> { inner: T | null = null; } value = new Box<string>();")]
    [InlineData("new Box<number>(); class Box<T> { inner: T | null = null; }")]
    public void ForwardGenericBindingKeepsConstraintsArgumentsAndValueOrder(string body)
    {
        string source = "interface Box<T> { outer: T; } function inspect() { " + body + " }";
        Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
    }
}
