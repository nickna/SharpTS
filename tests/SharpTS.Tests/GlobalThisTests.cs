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
    public void GlobalThis_AssignmentPreservesReadOnlyConstants(ExecutionMode mode)
    {
        const string source = """
            let calls = 0;
            globalThis.Infinity = ++calls;
            globalThis.NaN = ++calls;
            globalThis.undefined = ++calls;
            console.log(globalThis.Infinity, Number.isNaN(globalThis.NaN), globalThis.undefined, calls);
            function strictWrite() {
                "use strict";
                try { globalThis.Infinity = ++calls; } catch (e) { console.log(e.name); }
            }
            strictWrite();
            console.log(globalThis.Infinity, calls, Object.getOwnPropertyDescriptor(globalThis, "Infinity").writable);
            console.log(Reflect.set(globalThis, "Infinity", 3));
            """;
        Assert.Equal("Infinity true undefined 3\nTypeError\nInfinity 4 false\nfalse\n",
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void GlobalThis_OwnKeysIncludeNonEnumerableConstants(ExecutionMode mode)
    {
        const string source = """
            globalThis[3] = 3;
            globalThis.extra = 1;
            const names = Object.getOwnPropertyNames(globalThis);
            const constants = ["Infinity", "NaN", "undefined"];
            for (const key of constants) {
                console.log(names.includes(key), Reflect.ownKeys(globalThis).includes(key), Object.keys(globalThis).includes(key));
            }
            console.log(names.filter(k => k === "3" || constants.includes(k) || k === "extra").join(" "));
            """;
        Assert.Equal("true true false\ntrue true false\ntrue true false\n3 Infinity NaN undefined extra\n",
            TestHarness.RunUnchecked(source, mode));
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
    public void GlobalThis_DefinePropertyPreservesFixedConstants(ExecutionMode mode)
    {
        var result = TestHarness.Run("""
            for (const key of ["Infinity", "NaN", "undefined"]) {
                const value = globalThis[key];
                const keyCount = Object.getOwnPropertyNames(globalThis).length;
                try {
                    Object.defineProperty(globalThis, key, { value: 7 });
                } catch (e) { console.log(e.name); }
                console.log(Reflect.defineProperty(globalThis, key, {}),
                            Reflect.defineProperty(globalThis, key, { value }));
                console.log(Reflect.defineProperty(globalThis, key, { get: undefined }),
                            Reflect.defineProperty(globalThis, key, { set: undefined }),
                            Reflect.defineProperty(globalThis, key, { writable: true }),
                            Reflect.defineProperty(globalThis, key, { enumerable: true }),
                            Reflect.defineProperty(globalThis, key, { configurable: true }));
                const d = Object.getOwnPropertyDescriptor(globalThis, key);
                console.log(Object.is(value, globalThis[key]), Object.is(value, d.value),
                            d.writable, d.enumerable, d.configurable);
                console.log(Object.getOwnPropertyNames(globalThis).length === keyCount);
            }
            """, mode);
        const string expected = "TypeError\ntrue true\nfalse false false false false\ntrue true false false false\ntrue\n";
        Assert.Equal(expected + expected + expected, result);
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
    public void GlobalThis_BufferAccessorKeepsItsValueAndIdentity(ExecutionMode mode)
    {
        const string source = """
            const root: any = globalThis;
            const d = Object.getOwnPropertyDescriptor(root, "Buffer");
            const next = Object.getOwnPropertyDescriptor(root, "Buffer");
            console.log(typeof d.get, typeof d.set, d.enumerable, d.configurable, "value" in d);
            console.log(d.get === next.get, d.set === next.set, d.get() === Buffer);
            d.set(17);
            console.log(root.Buffer, globalThis.Buffer, Buffer);
            root.Buffer = null;
            console.log(d.get(), root.Buffer);
            Object.defineProperty(root, "Buffer", { value: 5 });
            d.set(9);
            console.log(root.Buffer, d.get(), delete root.Buffer, "Buffer" in root);
            """;
        Assert.Equal("function function false true false\ntrue true true\n17 17 17\nnull null\n5 9 true false\n",
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void GlobalThis_BufferPresenceFollowsDeletionAndRecreation(ExecutionMode mode)
    {
        var result = TestHarness.Run("""
            const root: any = globalThis;
            const key = "Buf" + "fer";
            console.log(key in root, Object.hasOwn(root, key), Reflect.has(root, key), typeof root[key]);
            console.log(delete root[key], key in root);
            root[key] = undefined;
            console.log(key in root, Object.hasOwn(root, key), typeof root[key]);
            """, mode);
        Assert.Equal("true true true function\ntrue false\ntrue true undefined\n", result);
    }

    [Theory, ModeData]
    public void GlobalThis_HasPropertyChecksPresenceWithoutReadingValues(ExecutionMode mode)
    {
        var result = TestHarness.Run("""
            let reads = 0;
            Object.defineProperty(globalThis, "trap", {
                get() { reads++; return undefined; }, configurable: true
            });
            Object.defineProperty(globalThis, "writeOnly", { set(value) {} });
            globalThis.empty = null;
            globalThis.ownUndefined = undefined;
            console.log("undefined" in globalThis, "trap" in globalThis, "empty" in globalThis,
                        "ownUndefined" in globalThis, "writeOnly" in globalThis, "missing" in globalThis);
            console.log(Reflect.has(globalThis, "undefined"), Reflect.has(globalThis, "trap"), reads);
            console.log("Array" in globalThis, "Infinity" in globalThis, "toString" in globalThis);
            delete globalThis.trap;
            console.log("trap" in globalThis);
            """, mode);
        Assert.Equal("true true true true true false\ntrue true 0\ntrue true true\nfalse\n", result);
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

    // Error constructors are realm-owned in interpreted mode and runtime type
    // tokens in compiled mode; either way globalThis and the bare identifier
    // must resolve to the same constructor object (#271, #1326).
    [Theory, ModeData]
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
    // the same real globalThis value in both execution modes.
    [Theory, ModeData]
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
