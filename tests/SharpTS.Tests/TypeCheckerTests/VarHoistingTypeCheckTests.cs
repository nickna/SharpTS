using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Tests that the type checker correctly handles JavaScript var hoisting semantics.
/// Var declarations are hoisted to the top of their enclosing scope, so forward
/// references from within function bodies should not produce false "Undefined variable" errors.
/// </summary>
public class VarHoistingTypeCheckTests
{
    [Fact]
    public void ForwardVarReference_InFunctionBody_Succeeds()
    {
        var source = """
            function f() { return x; }
            var x = 1;
            console.log(f());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public void ForwardVarReference_InFunctionExpression_Succeeds()
    {
        var source = """
            var getIt = function() { return _val; };
            var _val = 42;
            console.log(getIt());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void ForwardVarReference_InArrowFunction_Succeeds()
    {
        var source = """
            var getIt = () => _val;
            var _val = 42;
            console.log(getIt());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void ForwardVarReference_InObjectLiteralGetter_Succeeds()
    {
        // Simulates the Babel CJS interop pattern used by uuid and other packages
        var source = """
            var obj = {
                get value() { return _foo; }
            };
            var _foo = "bar";
            console.log(obj.value);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("bar", output.Trim());
    }

    [Fact]
    public void ForwardVarReference_DuplicateDeclarations_Succeeds()
    {
        var source = """
            function f() { return x; }
            var x = 1;
            var x = 2;
            console.log(f());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2", output.Trim());
    }

    // #533: a function declared BEFORE a let/const may reference that binding in its body — the
    // body only runs once the binding is initialized, so TypeScript (which collects every lexical
    // binding in a scope before checking bodies) accepts it. SharpTS now hoists let/const the same
    // way it hoists var and class. (A *direct* use-before-declaration is still rejected — at runtime
    // — because the binding isn't created until its statement executes; see the StillFails tests.)
    [Fact]
    public void ForwardLetReference_InFunctionBody_Succeeds()
    {
        var source = """
            function f() { return y; }
            let y = 1;
            console.log(f());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public void ForwardConstReference_InFunctionBody_Succeeds()
    {
        var source = """
            function f() { return z; }
            const z = 5;
            console.log(f());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("5", output.Trim());
    }

    [Fact]
    public void DirectLetReference_BeforeDeclaration_StillFailsAtRuntime()
    {
        // Not deferred behind a function: the read executes before `let x` runs, so the binding
        // does not exist yet. This stays an error (now surfaced at runtime, matching how a forward
        // class reference behaves), never silently producing undefined.
        var source = """
            console.log(x);
            let x = 1;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Undefined variable", ex.Message);
    }
}
