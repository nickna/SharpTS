using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for JSON.stringify of Proxy objects (#92).
/// Covers Proxy integration with JSON and reflective object operations.
/// </summary>
public class JSONProxyTests
{
    [Theory, ModeData]
    public void JSON_Stringify_ProxyWithGetTrap(ExecutionMode mode)
    {
        var source = """
            let target: any = { a: 1, b: 2 };
            let proxy: any = new Proxy(target, {
                get: function(t: any, p: string): any { return t[p] * 10; }
            });
            console.log(JSON.stringify(proxy));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\"a\":10,\"b\":20}\n", output);
    }

    [Theory, ModeData]
    public void JSON_Stringify_ProxyWithOwnKeysTrap(ExecutionMode mode)
    {
        var source = """
            let target: any = { a: 1, b: 2, c: 3 };
            let proxy: any = new Proxy(target, {
                ownKeys: function(t: any): string[] { return ["a", "c"]; }
            });
            console.log(JSON.stringify(proxy));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\"a\":1,\"c\":3}\n", output);
    }

    [Theory, ModeData]
    public void JSON_Stringify_RevokedProxy_Throws(ExecutionMode mode)
    {
        var source = """
            let target: any = { a: 1 };
            let r: any = Proxy.revocable(target, {});
            r.revoke();
            try {
                JSON.stringify(r.proxy);
                console.log("should not reach");
            } catch (e) {
                console.log("threw");
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("threw\n", output);
    }

    [Theory, CompiledOnlyData]
    public void Object_Keys_Proxy_DispatchesOwnKeysTrap(ExecutionMode mode)
    {
        var source = """
            let target: any = { a: 1, b: 2 };
            let proxy: any = new Proxy(target, {
                ownKeys: function(t: any): string[] { return ["x", "y"]; },
                getOwnPropertyDescriptor: function(t: any, key: string): any {
                    return { configurable: true, enumerable: true, value: key };
                }
            });
            console.log(Object.keys(proxy).join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("x,y\n", output);
    }

    [Theory, CompiledOnlyData]
    public void Object_GetOwnPropertyNames_Proxy_DispatchesOwnKeysTrap(ExecutionMode mode)
    {
        var source = """
            let target: any = { a: 1 };
            let proxy: any = new Proxy(target, {
                ownKeys: function(t: any): string[] { return ["p", "q", "r"]; }
            });
            console.log(Object.getOwnPropertyNames(proxy).join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("p,q,r\n", output);
    }

    [Theory, CompiledOnlyData]
    public void Object_Keys_RevokedProxy_Throws(ExecutionMode mode)
    {
        var source = """
            let target: any = { a: 1 };
            let r: any = Proxy.revocable(target, {});
            r.revoke();
            try {
                Object.keys(r.proxy);
                console.log("should not reach");
            } catch (e) {
                console.log("threw");
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("threw\n", output);
    }

    // ---- JSON.parse(reviver) — issue #92 final phase ----
    //
    // ECMA-262 25.5.1.1.1 InternalizeJSONProperty walks the parsed tree top-down
    // and mutates each holder IN PLACE via Set / Delete, calling the reviver
    // with `this` = holder. When a holder is a Proxy, those Set / Delete /
    // OwnKeys / Get operations dispatch to the proxy's traps. The tests below
    // pin the spec-required behavior in both interpreter and compiled modes.

    [Theory, ModeData]
    public void JSON_Parse_Reviver_ReceivesHolderAsThis(ExecutionMode mode)
    {
        // Reviver mutates `this` while walking — only possible if `this` is
        // bound to the in-progress holder (not null) AND the walker observes
        // the mutation when continuing to the sibling key.
        var source = """
            let observedSibling: any = "unset";
            JSON.parse('{"a":1,"b":2}', function (this: any, k: string, v: any): any {
                if (k === "a") {
                    this["b"] = 99;
                }
                if (k === "b") {
                    observedSibling = v;
                }
                return v;
            });
            console.log(observedSibling);
            """;

        var output = TestHarness.Run(source, mode);
        // After mutating this["b"] in the "a" reviver call, the walker
        // re-reads "b" from the holder — must observe 99, not the original 2.
        Assert.Equal("99\n", output);
    }

    [Theory, ModeData]
    public void JSON_Parse_Reviver_ArrayIndexKeyIsString(ExecutionMode mode)
    {
        // ECMA-262 step 2.b.iii.1: the reviver receives ToString(F(I)) for
        // array indices. The pre-fix implementation passed the raw double
        // index, which fails `typeof k === "string"`.
        var source = """
            let kinds: string = "";
            JSON.parse('[10, 20]', (k: any, v: any): any => {
                kinds += typeof k + ",";
                return v;
            });
            console.log(kinds);
            """;

        var output = TestHarness.Run(source, mode);
        // 3 calls: ("0", 10), ("1", 20), ("", root).
        Assert.Equal("string,string,string,\n", output);
    }

    [Theory, ModeData]
    public void JSON_Parse_Reviver_UndefinedRemovesObjectKey(ExecutionMode mode)
    {
        var source = """
            let result: any = JSON.parse('{"a":1,"b":2,"c":3}', (k: any, v: any): any => {
                if (k === "b") return undefined;
                return v;
            });
            console.log(JSON.stringify(result));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\"a\":1,\"c\":3}\n", output);
    }

    [Theory, CompiledOnlyData]
    public void JSON_Parse_Reviver_DispatchesProxyTraps(ExecutionMode mode)
    {
        // Reviver inserts a Proxy as a sibling value; the walker continues
        // into that Proxy, which must use its `get` and `ownKeys` traps to
        // enumerate and read children, and `set` to write the reviver's result.
        var source = """
            let trapLog: string = "";
            JSON.parse('{"a":1,"replaceMe":2}', function (this: any, k: string, v: any): any {
                if (k === "a") {
                    let target: any = { x: 100, y: 200 };
                    let proxy: any = new Proxy(target, {
                        get: function(t: any, p: string): any {
                            trapLog += "get:" + p + ";";
                            return t[p];
                        },
                        ownKeys: function(t: any): string[] {
                            trapLog += "ownKeys;";
                            return Object.keys(t);
                        },
                        set: function(t: any, p: string, val: any): boolean {
                            trapLog += "set:" + p + "=" + val + ";";
                            t[p] = val;
                            return true;
                        }
                    });
                    this["replaceMe"] = proxy;
                }
                return v;
            });
            console.log(trapLog);
            """;

        var output = TestHarness.Run(source, mode);
        // Expect ownKeys is called once, get is called for each key while
        // recursing in, and set is called for each key when storing the
        // reviver-returned value back. (`get` may fire more than twice if
        // intermediate paths read the proxy back.)
        Assert.Contains("ownKeys;", output);
        Assert.Contains("get:x;", output);
        Assert.Contains("get:y;", output);
        Assert.Contains("set:x=100;", output);
        Assert.Contains("set:y=200;", output);
    }

    [Theory, ModeData]
    public void JSON_Parse_Reviver_WithNullValue(ExecutionMode mode)
    {
        // Pins the null-guard in the IL ApplyReviver: when the parsed value
        // contains JS null, the iteration must skip the Proxy GetType() check
        // for that slot rather than NPE on null.GetType().
        var source = """
            let result: any = JSON.parse('{"a":null,"b":2}', (k: any, v: any): any => {
                if (k === "a" && v === null) return "was-null";
                return v;
            });
            console.log(result.a + "," + result.b);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("was-null,2\n", output);
    }

    [Theory, CompiledOnlyData]
    public void JSON_Parse_RevokedProxyDuringReviver_Throws(ExecutionMode mode)
    {
        // A revoked Proxy reached during the walk must throw. The trap
        // dispatch surfaces EnsureNotRevoked's TypeError as a regular
        // exception, which the user code can catch.
        var source = """
            let r: any = Proxy.revocable({ a: 1 }, {});
            let threw: boolean = false;
            try {
                JSON.parse('{"x":1, "y":2}', function (this: any, k: string, v: any): any {
                    if (k === "x") {
                        r.revoke();
                        this["y"] = r.proxy;
                    }
                    return v;
                });
            } catch (e) {
                threw = true;
            }
            console.log(threw);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }
}
