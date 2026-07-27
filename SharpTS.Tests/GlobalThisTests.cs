using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests;

/// <summary>
/// Tests for ES2020 globalThis support.
/// </summary>
public class GlobalThisTests
{
    [Theory, ModeData]
    public void GlobalThis_Math_MatchesDirect(ExecutionMode mode)
    {
        var result1 = TestHarness.Run("""
            // globalThis.Math.PI works like Math.PI
            console.log(globalThis.Math.PI === Math.PI);
            """, mode);
        Assert.Contains("true", result1);

        var result2 = TestHarness.Run("""
            // globalThis.Math.floor works like Math.floor
            console.log(globalThis.Math.floor(3.7));
            """, mode);
        Assert.Contains("3", result2);
    }

    [Theory, ModeData]
    public void GlobalThis_Console_Works(ExecutionMode mode)
    {
        var result = TestHarness.Run("""
            // globalThis.console.log works
            globalThis.console.log("Hello from globalThis");
            """, mode);
        Assert.Contains("Hello from globalThis", result);
    }

    [Theory, ModeData]
    public void GlobalThis_SelfReference(ExecutionMode mode)
    {
        var result = TestHarness.Run("""
            // Self-reference: globalThis.globalThis
            console.log(globalThis.globalThis === globalThis);
            """, mode);
        Assert.Contains("true", result);
    }

    [Theory, ModeData]
    public void GlobalThis_Assignment(ExecutionMode mode)
    {
        var result = TestHarness.Run("""
            // Assignment to globalThis
            globalThis.myValue = 42;
            console.log(globalThis.myValue);
            """, mode);
        Assert.Contains("42", result);
    }

    [Theory, ModeData]
    public void GlobalThis_IndexAccess(ExecutionMode mode)
    {
        var result = TestHarness.Run("""
            // Index access with string literal
            globalThis["testProp"] = "hello";
            console.log(globalThis["testProp"]);
            """, mode);
        Assert.Contains("hello", result);
    }

    [Theory, ModeData]
    public void GlobalThis_DynamicIndex(ExecutionMode mode)
    {
        var result = TestHarness.Run("""
            // Dynamic index access
            const name: string = "Math";
            console.log(typeof globalThis[name]);
            """, mode);
        Assert.Contains("object", result);
    }

    [Theory, ModeData]
    public void GlobalThis_BuiltInConstants(ExecutionMode mode)
    {
        var r1 = TestHarness.Run("""
            // globalThis.undefined equals undefined
            console.log(globalThis.undefined === undefined);
            """, mode);
        Assert.Contains("true", r1);

        var r2 = TestHarness.Run("""
            // globalThis.NaN is NaN
            console.log(Number.isNaN(globalThis.NaN));
            """, mode);
        Assert.Contains("true", r2);

        var r3 = TestHarness.Run("""
            // globalThis.Infinity is Infinity
            console.log(globalThis.Infinity === Infinity);
            """, mode);
        Assert.Contains("true", r3);
    }

    [Theory, ModeData]
    public void GlobalThis_ModuleScopedVarsNotAccessible(ExecutionMode mode)
    {
        var result = TestHarness.Run("""
            // User-defined variables are NOT accessible via globalThis (module semantics)
            let x: number = 1;
            console.log(globalThis.x === undefined);
            """, mode);
        Assert.Contains("true", result);
    }

    [Theory, ModeData]
    public void GlobalThis_ChainedSelfReference(ExecutionMode mode)
    {
        var result = TestHarness.Run("""
            // Chained self-reference: globalThis.globalThis.Math.PI
            console.log(globalThis.globalThis.Math.PI === Math.PI);
            """, mode);
        Assert.Contains("true", result);
    }

    [Theory, ModeData]
    public void GlobalThis_Process(ExecutionMode mode)
    {
        var result = TestHarness.Run("""
            // Process access through globalThis
            console.log(typeof globalThis.process);
            """, mode);
        Assert.Contains("object", result);
    }

    [Theory, ModeData]
    public void GlobalThis_ParseInt(ExecutionMode mode)
    {
        var result = TestHarness.Run("""
            // globalThis.parseInt works
            console.log(globalThis.parseInt("42"));
            """, mode);
        Assert.Contains("42", result);
    }

    // #271: globalThis used in VALUE position (assigned to a variable, then
    // dereferenced) must be a real object, not null. Before the fix compiled
    // mode emitted null, so root/context detection in lodash-style packages got
    // a null root and could not initialize.
    [Theory, ModeData]
    public void GlobalThis_ValuePosition_IsRealObject(ExecutionMode mode)
    {
        var result = TestHarness.Run("""
            const root: any = globalThis;
            console.log(typeof root === "object");
            console.log(root !== null);
            console.log(root.globalThis === root);
            """, mode);
        Assert.Equal("true\ntrue\ntrue\n", result);
    }

    // Cross-mode: typeof checks and Object identity hold in both modes.
    [Theory, ModeData]
    public void GlobalThis_ValuePosition_ResolvesConstructorTypeofs(ExecutionMode mode)
    {
        var result = TestHarness.Run("""
            const root: any = globalThis;
            console.log(root.Object === Object);
            console.log(typeof root.Array === "function");
            console.log(typeof root.Function === "function");
            console.log(typeof root.TypeError === "function");
            console.log(typeof root.Math === "object");
            """, mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\n", result);
    }

    // Compiled-only: the runtime sentinel routes constructor reads to real .NET
    // Type tokens, so `root.Error === Error` holds (#271). The interpreter's
    // SharpTSGlobalThis returns a distinct Error representation for this identity,
    // which is an independent pre-existing quirk outside this fix's scope.
    [Theory, CompiledOnlyData]
    public void GlobalThis_ValuePosition_ResolvesErrorConstructorIdentity(ExecutionMode mode)
    {
        var result = TestHarness.Run("""
            const root: any = globalThis;
            console.log(root.Error === Error);
            console.log(root.TypeError === TypeError);
            """, mode);
        Assert.Equal("true\ntrue\n", result);
    }

    // #271: writes through a value-position globalThis reference round-trip to
    // subsequent reads (the lodash `root.foo = ...` pattern).
    [Theory, ModeData]
    public void GlobalThis_ValuePosition_WriteRoundTrips(ExecutionMode mode)
    {
        var result = TestHarness.Run("""
            const root: any = globalThis;
            root.myProp = 42;
            console.log(globalThis.myProp === 42);
            root["dynKey"] = "hi";
            console.log(globalThis.dynKey === "hi");
            """, mode);
        Assert.Equal("true\ntrue\n", result);
    }

    // #271: the lodash/core-js `Function('return this')()` global probe yields
    // the same real globalThis value (compiled mode; the interpreter rejects the
    // dynamic Function constructor, so this is compiled-only).
    [Theory, CompiledOnlyData]
    public void GlobalThis_FunctionReturnThisProbe_IsGlobalThis(ExecutionMode mode)
    {
        var result = TestHarness.Run("""
            const probe: any = Function('return this')();
            console.log(probe === globalThis);
            console.log(probe.Object === Object);
            """, mode);
        Assert.Equal("true\ntrue\n", result);
    }
}
