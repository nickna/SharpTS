using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Coverage for <c>noImplicitAny</c> (TS7006 / TS7019).
/// </summary>
/// <remarks>
/// SharpTS reports only for parameters of DECLARED functions, methods and constructors — never
/// for arrows or function expressions, because contextual parameter typing covers only some
/// callee shapes (see <c>TypeChecker.ReportImplicitAnyParameters</c>). The negative cases below
/// are the guard on that decision: they must keep passing, or the flag becomes unusable on
/// idiomatic code.
/// </remarks>
public class NoImplicitAnyTests
{
    private static readonly TypeCheckerOptions On =
        TypeCheckerOptions.Default with { NoImplicitAny = true, MaxErrors = 50 };

    private static IReadOnlyList<Diagnostic> Diagnose(string source, TypeCheckerOptions? options = null)
    {
        var tokens = new Lexer(source).ScanTokens();
        var parsed = new Parser(tokens).Parse();
        Assert.True(parsed.IsSuccess);
        return new TypeChecker(options ?? On).CheckWithRecovery(parsed.Statements).Diagnostics;
    }

    private static IReadOnlyList<Diagnostic> ImplicitAny(string source, TypeCheckerOptions? options = null) =>
        Diagnose(source, options).Where(d => d.TsCode is "TS7006" or "TS7019").ToList();

    #region Reported

    [Fact]
    public void FunctionDeclaration_UnannotatedParameter_ReportsTS7006()
    {
        var d = Assert.Single(ImplicitAny("function f(x) { return x; }"));

        Assert.Equal("TS7006", d.TsCode);
        Assert.Equal(1, d.Line);
        Assert.Contains("Parameter 'x' implicitly has an 'any' type", d.Message);
    }

    [Fact]
    public void Method_UnannotatedParameters_ReportEach()
    {
        var diagnostics = ImplicitAny("class C { m(a, b) { return 0; } }");

        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Equal("TS7006", d.TsCode));
    }

    [Fact]
    public void Constructor_UnannotatedParameter_Reports()
    {
        Assert.Single(ImplicitAny("class C { constructor(a) { } }"));
    }

    [Fact]
    public void RestParameter_ReportsTS7019_NotTS7006()
    {
        var d = Assert.Single(ImplicitAny("function f(...rest) { return rest; }"));

        Assert.Equal("TS7019", d.TsCode);
        Assert.Contains("implicitly has an 'any[]' type", d.Message);
    }

    [Fact]
    public void OptionalParameter_IsReported_MatchingTsc()
    {
        // tsc emits TS7006 for `function f(x?)`, so SharpTS does too.
        Assert.Single(ImplicitAny("function f(x?) { return x; }"));
    }

    [Fact]
    public void OnlyTheUnannotatedParametersAreReported()
    {
        var d = Assert.Single(ImplicitAny("function f(a: number, b, c: string) { return a; }"));

        Assert.Contains("'b'", d.Message);
    }

    #endregion

    #region Not reported — the false-positive guards

    [Fact]
    public void ArrowArgument_IsNotReported()
    {
        // Already contextually typed by TypeChecker.Calls.cs, and the exclusion covers the
        // callee shapes where that contextual typing does not reach.
        Assert.Empty(ImplicitAny("const a = [1, 2, 3]; a.map(x => x * 2);"));
    }

    [Fact]
    public void PromiseThenCallback_IsNotReported()
    {
        // `then`'s parameter is typed `Function | undefined`, which contextual typing does not
        // unwrap — this is exactly the case that would false-positive.
        Assert.Empty(ImplicitAny("const p: Promise<number> = Promise.resolve(1); p.then(v => v);"));
    }

    [Fact]
    public void CallbackOnAnyTypedCallee_IsNotReported()
    {
        // console/Math/JSON/fetch are hardcoded `any`, so nothing can contextually type these.
        Assert.Empty(ImplicitAny("const xs = [1]; xs.forEach(function (v) { console.log(v); });"));
    }

    [Fact]
    public void ArrowAssignedToConst_IsNotReported()
    {
        // A documented divergence: tsc DOES report here. Under-reporting is the safe direction.
        Assert.Empty(ImplicitAny("const f = (x) => x;"));
    }

    [Fact]
    public void ParameterWithDefault_IsNotReported()
    {
        // The type comes from the initializer.
        Assert.Empty(ImplicitAny("function f(x = 0) { return x; }"));
    }

    [Fact]
    public void ThisParameter_IsNotReported()
    {
        // The parser routes `this: T` to Stmt.Function.ThisType, never into Parameters.
        Assert.Empty(ImplicitAny("class Foo { }\nfunction f(this: Foo, x: number) { return x; }"));
    }

    [Fact]
    public void DestructuredParameter_IsNotReported()
    {
        // Collapses to a synthetic `_paramN` binding; per-element names (TS7031) do not exist.
        Assert.Empty(ImplicitAny("function f({ a, b }) { return a; }"));
    }

    [Fact]
    public void AmbientDeclaration_IsNotReported()
    {
        Assert.Empty(ImplicitAny("declare function f(x): void;"));
    }

    [Fact]
    public void AnnotatedParameters_AreNotReported()
    {
        Assert.Empty(ImplicitAny("function f(x: number, y: string) { return x; }"));
    }

    #endregion

    #region Off by default — the regression guard

    [Theory]
    [InlineData("function f(x) { return x; }")]
    [InlineData("class C { m(a, b) { return 0; } }")]
    [InlineData("class C { constructor(a) { } }")]
    [InlineData("function f(...rest) { return rest; }")]
    [InlineData("function f(x?) { return x; }")]
    public void ProductDefaults_ReportNothing(string source)
    {
        Assert.Empty(ImplicitAny(source, TypeCheckerOptions.Default with { MaxErrors = 50 }));
    }

    #endregion

    #region No duplicates

    [Fact]
    public void TopLevelFunction_ReportsExactlyOnce()
    {
        // Hoisting and checking both build the signature; only the check path may report.
        Assert.Single(ImplicitAny("function f(x) { return x; }"));
    }

    [Fact]
    public void NestedFunction_ReportsExactlyOnce()
    {
        // HoistFunctionDeclarations re-runs for every enclosing function body.
        var diagnostics = ImplicitAny("""
            function outer() {
                function inner(x) { return x; }
                return inner;
            }
            """);

        Assert.Single(diagnostics);
        Assert.Equal(2, diagnostics[0].Line);
    }

    [Fact]
    public void ForwardReferencedFunction_ReportsExactlyOnce()
    {
        // Drives the speculative return-type inference that sets _suppressDiagnostics.
        var diagnostics = ImplicitAny("""
            function outer() {
                return helper(1);
                function helper(v) { return v; }
            }
            """);

        Assert.Single(diagnostics);
    }

    #endregion

    #region Umbrella

    [Fact]
    public void StrictUmbrella_EnablesIt()
    {
        Assert.Single(ImplicitAny("function f(x) { return x; }", TypeCheckerOptions.Strict));
    }

    #endregion
}
