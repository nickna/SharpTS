using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ForwardClassTypeAnnotationTests
{
    [Theory, ModeData]
    public void AnnotationCanReadMembersBeforeAnyConstructorReference(ExecutionMode mode)
    {
        const string source = """
            interface Later { known: string; }
            function outer() {
                class Earlier { inspect(value: Later): string { return value.known; } }
                class Later { known: string = "ok"; }
                return new Earlier().inspect(new Later());
            }
            console.log(outer());
            """;
        Assert.Equal("ok\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AnnotationBeforeLocalClassShadowsOuterInterface(ExecutionMode mode)
    {
        const string source = """
            interface Params { outer: string; }
            function read() {
                class Holder {
                    value: Params | null = null;
                    create() { this.value = new Params(); }
                }
                class Params { value: number = 7; }
                const holder = new Holder();
                holder.create();
                return holder.value!.value;
            }
            const outer: Params = { outer: "outer" };
            console.log(read(), outer.outer);
            """;
        Assert.Equal("7 outer\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void ForwardAnnotationRetainsTheLocalClassMemberType()
    {
        const string source = """
            interface Params { value: number; }
            function read() {
                class Holder {
                    value: Params | null = null;
                    create() { this.value = new Params(); }
                }
                class Params { value: string = "local"; }
                const holder = new Holder();
                holder.create();
                const invalid: number = holder.value!.value;
            }
            """;
        var error = Assert.ThrowsAny<TypeCheckException>(() =>
            new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal("TS2322", error.Diagnostic.TsCode);
    }

    [Fact]
    public void GenericLocalClassRetainsItsTypeParameter()
    {
        const string source = """
            interface Params { outer: string; }
            function read() {
                class Params<T> { constructor(public value: T) {} }
                const invalid: number = new Params<string>("local").value;
            }
            """;
        var error = Assert.ThrowsAny<TypeCheckException>(() =>
            new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal("TS2322", error.Diagnostic.TsCode);
    }
}
