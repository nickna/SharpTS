using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for JavaScript Proxy object functionality.
/// Runs against both interpreter and compiler.
/// </summary>
public class ProxyTests
{
    [Theory, ModeData]
    public void Proxy_ArrayConcat_PreservesSymbolKeysAndArrayClassification(ExecutionMode mode)
    {
        var source = """
            const arrayProxy: any = new Proxy([1, 2], {});
            const nestedProxy: any = new Proxy(arrayProxy, {});
            console.log([0].concat(arrayProxy).join(","));
            console.log([0].concat(nestedProxy).join(","));

            let sawSymbol = false;
            const arrayLike: any = { 0: "x", length: 1 };
            const spreadableProxy: any = new Proxy(arrayLike, {
                get(target: any, key: any) {
                    if (key === Symbol.isConcatSpreadable) {
                        sawSymbol = true;
                        return true;
                    }
                    return target[key];
                }
            });
            console.log([].concat(spreadableProxy).join(","));
            console.log(sawSymbol);

            const handle: any = Proxy.revocable([], {});
            handle.revoke();
            try {
                [].concat(handle.proxy);
            } catch (error) {
                console.log(error instanceof TypeError);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0,1,2\n0,1,2\nx\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Proxy_GetTrap(ExecutionMode mode)
    {
        var source = @"
            const target = { name: ""world"" };
            const handler = {
                get(target: any, prop: string, receiver: any) {
                    if (prop === ""name"") return ""intercepted"";
                    return target[prop];
                }
            };
            const p = new Proxy(target, handler);
            console.log(p.name);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("intercepted\n", output);
    }

    [Theory, ModeData]
    public void Proxy_SetTrap(ExecutionMode mode)
    {
        var source = @"
            const target: any = { count: 0 };
            const handler = {
                set(target: any, prop: string, value: any, receiver: any) {
                    target[prop] = value * 2;
                    return true;
                }
            };
            const p: any = new Proxy(target, handler);
            p.count = 5;
            console.log(target.count);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory, ModeData]
    public void Proxy_HasTrap(ExecutionMode mode)
    {
        var source = @"
            const target = { a: 1, b: 2 };
            const handler = {
                has(target: any, prop: string) {
                    if (prop === ""c"") return true;
                    return prop in target;
                }
            };
            const p = new Proxy(target, handler);
            console.log(""a"" in p);
            console.log(""c"" in p);
            console.log(""d"" in p);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\nfalse\n", output);
    }

    [Theory, ModeData]
    public void Proxy_DeletePropertyTrap(ExecutionMode mode)
    {
        var source = @"
            const target: any = { x: 1, y: 2 };
            let deletedProp = """";
            const handler = {
                deleteProperty(target: any, prop: string) {
                    deletedProp = prop;
                    delete target[prop];
                    return true;
                }
            };
            const p: any = new Proxy(target, handler);
            delete p.x;
            console.log(deletedProp);
            console.log(target.x);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("x\nundefined\n", output);
    }

    [Theory, ModeData]
    public void Proxy_DefaultForwarding_Get(ExecutionMode mode)
    {
        var source = @"
            const target = { name: ""original"" };
            const handler = {};
            const p = new Proxy(target, handler);
            console.log(p.name);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("original\n", output);
    }

    [Theory, ModeData]
    public void Proxy_DefaultForwarding_Set(ExecutionMode mode)
    {
        var source = @"
            const target: any = { count: 0 };
            const handler = {};
            const p: any = new Proxy(target, handler);
            p.count = 42;
            console.log(target.count);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void Proxy_Typeof(ExecutionMode mode)
    {
        var source = @"
            const target = { x: 1 };
            const handler = {};
            const p = new Proxy(target, handler);
            console.log(typeof p);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\n", output);
    }

    [Theory, ModeData]
    public void Proxy_ApplyTrap(ExecutionMode mode)
    {
        var source = @"
            function greet(name: string): string {
                return ""Hello, "" + name;
            }
            const handler = {
                apply(target: any, thisArg: any, args: any[]) {
                    return target(args[0]) + ""!"";
                }
            };
            const p = new Proxy(greet, handler);
            console.log(p(""World""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello, World!\n", output);
    }

    [Theory, ModeData]
    public void Proxy_TypeofFunction(ExecutionMode mode)
    {
        var source = @"
            function myFunc(): number { return 1; }
            const handler = {};
            const p = new Proxy(myFunc, handler);
            console.log(typeof p);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\n", output);
    }

    [Theory, ModeData]
    public void Proxy_HasTrap_TruthyCoercion(ExecutionMode mode)
    {
        var source = @"
            const target = { a: 1 };
            const handler = {
                has(target: any, prop: string) {
                    if (prop === ""x"") return 1;
                    if (prop === ""y"") return 0;
                    if (prop === ""z"") return ""yes"";
                    return false;
                }
            };
            const p = new Proxy(target, handler);
            console.log(""x"" in p);
            console.log(""y"" in p);
            console.log(""z"" in p);
            console.log(""w"" in p);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\ntrue\nfalse\n", output);
    }

    [Theory, ModeData]
    public void Proxy_Revocable(ExecutionMode mode)
    {
        var source = @"
            const target = { value: 42 };
            const result = Proxy.revocable(target, {});
            const proxy = result.proxy;
            const revoke = result.revoke;
            console.log(proxy.value);
            revoke();
            try {
                console.log(proxy.value);
            } catch(e) {
                console.log(""revoked"");
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\nrevoked\n", output);
    }

    [Theory, ModeData]
    public void Proxy_RevokedThrows(ExecutionMode mode)
    {
        var source = @"
            const target: any = { x: 1 };
            const result = Proxy.revocable(target, {});
            const proxy: any = result.proxy;
            const revoke = result.revoke;
            revoke();
            let caught = false;
            try {
                proxy.x = 5;
            } catch(e) {
                caught = true;
            }
            console.log(caught);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Proxy_GetTrap_MultipleProperties(ExecutionMode mode)
    {
        var source = @"
            const target = { a: 1, b: 2 };
            const log: string[] = [];
            const handler = {
                get(target: any, prop: string) {
                    log.push(prop);
                    return target[prop];
                }
            };
            const p = new Proxy(target, handler);
            const x = p.a;
            const y = p.b;
            console.log(log.join("",""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("a,b\n", output);
    }

    [Theory, ModeData]
    public void Proxy_SetTrap_ReturnsValue(ExecutionMode mode)
    {
        var source = @"
            const target: any = {};
            const handler = {
                set(target: any, prop: string, value: any) {
                    target[prop] = value + 1;
                    return true;
                }
            };
            const p: any = new Proxy(target, handler);
            p.x = 10;
            console.log(p.x);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("11\n", output);
    }

    [Theory, ModeData]
    public void Proxy_NestedTraps(ExecutionMode mode)
    {
        var source = @"
            const inner = { value: ""base"" };
            const innerProxy = new Proxy(inner, {
                get(target: any, prop: string) {
                    if (prop === ""value"") return ""inner_"" + target[prop];
                    return target[prop];
                }
            });
            const outerProxy = new Proxy(innerProxy, {
                get(target: any, prop: string) {
                    return ""outer_"" + target[prop];
                }
            });
            console.log(outerProxy.value);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("outer_inner_base\n", output);
    }

    [Theory, ModeData]
    public void Proxy_OwnDescriptorOperations_UseDedicatedTraps(ExecutionMode mode)
    {
        var source = """
            let ordinaryGets = 0;
            const proxy: any = new Proxy({}, {
                ownKeys(): string[] { return ['hidden']; },
                getOwnPropertyDescriptor(): any { return undefined; },
                get(): any { ordinaryGets++; throw new Error('unexpected get'); }
            });
            console.log(Reflect.ownKeys(proxy).join(','));
            console.log(Object.getOwnPropertyDescriptor(proxy, 'hidden') === undefined);
            console.log(Object.keys(Object.getOwnPropertyDescriptors(proxy)).length);
            console.log(ordinaryGets);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hidden\ntrue\n0\n0\n", output);
    }
}
