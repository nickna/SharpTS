using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression for a compiler gap surfaced by the Phase 3i util migration:
/// property access like <c>map.get</c> on a Map received across a module
/// boundary returned <c>null</c> in compiled mode, so <c>typeof map.get</c>
/// evaluated to <c>'object'</c> instead of <c>'function'</c>.
/// </summary>
/// <remarks>
/// Fixed by emitting <c>$BoundMapMethod</c> / <c>$BoundSetMethod</c> runtime
/// types that wrap (collection, method-name) pairs (mirrors <c>$BoundArrayMethod</c>)
/// and wiring <c>GetMapProperty</c> / <c>GetSetProperty</c> helpers into the
/// <c>GetProperty</c> dispatcher so dynamic access on <c>Dictionary&lt;object,object&gt;</c>
/// and <c>HashSet&lt;object&gt;</c> returns a callable that reports
/// <c>typeof === 'function'</c> and dispatches via <c>InvokeValue</c> /
/// <c>InvokeMethodValue</c>.
/// </remarks>
public class MapSetDuckTypingTests
{
    [Theory, ModeData]
    public void Map_TypeofMethodsIsFunction(ExecutionMode mode)
    {
        var source = @"
            const m = new Map<string, number>();
            m.set('a', 1);
            console.log(typeof m.get);
            console.log(typeof m.set);
            console.log(typeof m.has);
            console.log(typeof m.delete);
            console.log(typeof m.clear);
            console.log(typeof m.keys);
            console.log(typeof m.values);
            console.log(typeof m.entries);
            console.log(typeof m.forEach);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal(
            "function\nfunction\nfunction\nfunction\nfunction\nfunction\nfunction\nfunction\nfunction\n",
            output);
    }

    [Theory, ModeData]
    public void Set_TypeofMethodsIsFunction(ExecutionMode mode)
    {
        var source = @"
            const s = new Set<number>();
            s.add(1);
            console.log(typeof s.add);
            console.log(typeof s.has);
            console.log(typeof s.delete);
            console.log(typeof s.clear);
            console.log(typeof s.keys);
            console.log(typeof s.values);
            console.log(typeof s.entries);
            console.log(typeof s.forEach);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal(
            "function\nfunction\nfunction\nfunction\nfunction\nfunction\nfunction\nfunction\n",
            output);
    }

    [Theory, ModeData]
    public void Map_SizeIsNumber(ExecutionMode mode)
    {
        var source = @"
            const m = new Map<string, number>();
            m.set('a', 1);
            m.set('b', 2);
            console.log(typeof m.size);
            console.log(m.size);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("number\n2\n", output);
    }

    [Theory, ModeData]
    public void Set_SizeIsNumber(ExecutionMode mode)
    {
        var source = @"
            const s = new Set<number>();
            s.add(10);
            s.add(20);
            console.log(typeof s.size);
            console.log(s.size);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("number\n2\n", output);
    }

    [Theory, ModeData]
    public void Map_CapturedMethodDirectInvocation(ExecutionMode mode)
    {
        var source = @"
            const m = new Map<string, number>();
            m.set('x', 42);
            m.set('y', 7);
            const g = m.get;
            console.log(g('x'));
            console.log(g('y'));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n7\n", output);
    }

    [Theory, ModeData]
    public void Set_CapturedMethodDirectInvocation(ExecutionMode mode)
    {
        var source = @"
            const s = new Set<number>();
            s.add(5);
            s.add(6);
            const h = s.has;
            console.log(h(5));
            console.log(h(99));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory, ModeData]
    public void Map_CrossModule_DuckTypingCheck(ExecutionMode mode)
    {
        // The util.types.isMap polyfill pattern — exactly what the util migration
        // needed to work across module boundaries.
        var files = new Dictionary<string, string>
        {
            ["lib.ts"] = """
                export function makeMap(): unknown {
                    const m = new Map<string, number>();
                    m.set('one', 1);
                    m.set('two', 2);
                    return m;
                }
                """,
            ["main.ts"] = """
                import { makeMap } from './lib';
                function isMap(x: any): boolean {
                    return x != null
                        && typeof x.get === 'function'
                        && typeof x.set === 'function'
                        && typeof x.has === 'function'
                        && typeof x.size === 'number';
                }
                const m: any = makeMap();
                console.log(isMap(m));
                console.log(isMap({}));
                console.log(isMap(null));
                console.log(m.get('one'));
                console.log(m.size);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\nfalse\nfalse\n1\n2\n", output);
    }

    [Theory, ModeData]
    public void Set_CrossModule_DuckTypingCheck(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["lib.ts"] = """
                export function makeSet(): unknown {
                    const s = new Set<number>();
                    s.add(100);
                    s.add(200);
                    return s;
                }
                """,
            ["main.ts"] = """
                import { makeSet } from './lib';
                function isSet(x: any): boolean {
                    return x != null
                        && typeof x.add === 'function'
                        && typeof x.has === 'function'
                        && typeof x.delete === 'function'
                        && typeof x.size === 'number';
                }
                const s: any = makeSet();
                console.log(isSet(s));
                console.log(isSet({}));
                console.log(isSet(null));
                console.log(s.has(100));
                console.log(s.size);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\nfalse\nfalse\ntrue\n2\n", output);
    }

    [Theory, ModeData]
    public void Map_CrossModule_DynamicMethodAccess(ExecutionMode mode)
    {
        // Receiving a Map typed as `unknown` / `any` must still allow dynamic
        // method access (m.get/m.set/...) in the consumer module.
        var files = new Dictionary<string, string>
        {
            ["producer.ts"] = """
                export function produce(): unknown {
                    const m = new Map<string, number>();
                    m.set('k', 99);
                    return m;
                }
                """,
            ["main.ts"] = """
                import { produce } from './producer';
                const m: any = produce();
                console.log(m.get('k'));
                console.log(m.has('k'));
                console.log(m.has('missing'));
                m.set('new', 1);
                console.log(m.size);
                m.delete('k');
                console.log(m.has('k'));
                console.log(m.size);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("99\ntrue\nfalse\n2\nfalse\n1\n", output);
    }

    [Theory, ModeData]
    public void Set_CrossModule_DynamicMethodAccess(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["producer.ts"] = """
                export function produce(): unknown {
                    const s = new Set<string>();
                    s.add('red');
                    s.add('blue');
                    return s;
                }
                """,
            ["main.ts"] = """
                import { produce } from './producer';
                const s: any = produce();
                console.log(s.has('red'));
                console.log(s.has('green'));
                s.add('green');
                console.log(s.size);
                s.delete('red');
                console.log(s.has('red'));
                console.log(s.size);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\nfalse\n3\nfalse\n2\n", output);
    }

}
