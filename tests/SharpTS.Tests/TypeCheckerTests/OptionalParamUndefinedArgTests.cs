using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// #668: an optional (<c>x?: T</c>) or default-valued (<c>x: T = ...</c>) parameter accepts an
/// explicit <c>undefined</c> argument at the call site (its call-site type is <c>T | undefined</c>);
/// passing <c>undefined</c> to a default-valued parameter triggers the default. A genuinely required
/// parameter must still reject <c>undefined</c>. Runs ahead of both interpreter and compiler.
/// </summary>
public class OptionalParamUndefinedArgTests
{
    [Theory, ModeData]
    public void OptionalParameter_AcceptsExplicitUndefined(ExecutionMode mode)
    {
        var source = """
            function f(name?: string): string { return "Hi " + name; }
            console.log(f(undefined));
            """;
        Assert.Equal("Hi undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DefaultParameter_AcceptsExplicitUndefined_FiresDefault(ExecutionMode mode)
    {
        var source = """
            function g(n: string = "W"): string { return n; }
            console.log(g(undefined));
            """;
        Assert.Equal("W\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void DefaultSecondParameter_AcceptsExplicitUndefined()
    {
        // Interpreter-only: the #668 type-check fix (accept explicit `undefined`) is exercised in
        // BOTH modes by the optional/string-default tests above. Passing `undefined` to a *value-type*
        // default of a function declaration is correct here but yields NaN in compiled mode — a
        // separate value-type-slot runtime gap tracked in #705.
        var source = """
            function p(a: string, b: number = 2): number { return a.length + b; }
            console.log(p("x", undefined));
            """;
        Assert.Equal("3\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void RequiredParameter_RejectsUndefined()
    {
        // Regression guard: a genuinely required parameter must still reject `undefined` (TS2345).
        var source = """
            function r(n: string): string { return n; }
            console.log(r(undefined));
            """;
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void OptionalParameter_AcceptsUnionWithUndefinedArgument()
    {
        // `string | undefined` value passed to an optional `string` parameter.
        var source = """
            function f(name?: string): string { return typeof name; }
            const v: string | undefined = (Math.random() < 0 ? "x" : undefined);
            console.log(f(v));
            """;
        // Type-checks (no TS2345); value is undefined at runtime.
        Assert.Equal("undefined\n", TestHarness.RunInterpreted(source));
    }

    [Theory, ModeData]
    public void PrivateMethodDefaultParameter_AcceptsUndefined(ExecutionMode mode)
    {
        // #668 private-method argument-compatibility path. Runs in BOTH modes: unlike free
        // functions / constructors (#705), compiled private methods emit every parameter as an
        // `object` slot, so the value-type default is not stranded in a `double` slot — the
        // call-site `undefined` padding + body default prologue added in #696 fire the default.
        var source = """
            class C {
              #priv(a: string, b: number = 7): number { return a.length + b; }
              go(): number { return this.#priv("x", undefined); }
            }
            console.log(new C().go());
            """;
        Assert.Equal("8\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void PrivateMethodOptionalParameter_AcceptsExplicitUndefined(ExecutionMode mode)
    {
        // #668 path that #696 unblocked: an *optional* (`b?: T`) private-method parameter could
        // not previously be parsed, so the original #668 work had to probe this path with a
        // default param instead. An optional reference param accepts an explicit `undefined`.
        var source = """
            class C {
              #priv(a: string, b?: string): string { return a + (b ?? "_"); }
              go(): string { return this.#priv("x", undefined); }
            }
            console.log(new C().go());
            """;
        Assert.Equal("x_\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void PrivateMethodOptionalParameter_OmittedArgument(ExecutionMode mode)
    {
        // The minimal #696 scenario end-to-end: a private method with a sole optional parameter
        // invoked with no argument. The omitted slot reads as `undefined` in both modes.
        var source = """
            class C {
              #priv(b?: number): number { return b ?? 1; }
              go(): number { return this.#priv(); }
            }
            console.log(new C().go());
            """;
        Assert.Equal("1\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void ConstructorDefaultParameter_AcceptsUndefined()
    {
        // Interpreter-only — see DefaultSecondParameter_AcceptsExplicitUndefined; value-type
        // default in a constructor yields NaN in compiled mode (tracked in #705).
        var source = """
            class D {
              constructor(public name: string, public age: number = 99) {}
            }
            const d = new D("Bob", undefined);
            console.log(d.name + ":" + d.age);
            """;
        Assert.Equal("Bob:99\n", TestHarness.RunInterpreted(source));
    }
}
