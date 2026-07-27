using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression for <c>.call</c> / <c>.apply</c> / <c>.bind</c> on the callable wrappers
/// returned by dynamic array/map/set method access (e.g. <c>const push = arr.push; push.call(arr, 1)</c>).
/// This used to be a pre-existing limitation — the wrappers had no property handlers for
/// bind/call/apply/length/name and the wrappers' Invoke bodies only knew how to dispatch
/// <c>$TSFunction</c>/<c>$BoundTSFunction</c> targets.
/// </summary>
/// <remarks>
/// Fixed by: routing GetProperty on the bound-method types through GetFunctionMethod,
/// extending $FunctionCallWrapper / $FunctionApplyWrapper with a bound-target dispatch
/// chain (EmitDispatchToTarget), and introducing $BoundAnyFunction as the partial-apply
/// result for <c>.bind</c> on any non-$TSFunction callable. Also fixes a pre-existing
/// $BoundArrayMethod bug where push/unshift/indexOf/includes/concat were passing the
/// whole args array as a single element, and extends direct `arr.push(a, b, c)` /
/// `arr.unshift(a, b, c)` to correctly push all elements (matching JS variadic semantics).
/// </remarks>
public class BoundMethodCallApplyBindTests
{
    [Theory, ModeData]
    public void Array_Push_VariadicDirect(ExecutionMode mode)
    {
        // Pre-existing bug: arr.push(a, b, c) in compiled mode only pushed the first arg.
        var source = @"
            const arr: number[] = [1];
            arr.push(10, 20, 30);
            console.log(arr.length);
            console.log(arr[1]);
            console.log(arr[2]);
            console.log(arr[3]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n10\n20\n30\n", output);
    }

    [Theory, ModeData]
    public void Array_Unshift_VariadicDirect(ExecutionMode mode)
    {
        // JS `arr.unshift(a, b, c)` on [x,y] yields [a,b,c,x,y].
        var source = @"
            const arr: number[] = [4, 5];
            arr.unshift(1, 2, 3);
            console.log(arr.length);
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
            console.log(arr[3]);
            console.log(arr[4]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n1\n2\n3\n4\n5\n", output);
    }

    [Theory, ModeData]
    public void Array_Push_CallSingleArg(ExecutionMode mode)
    {
        var source = @"
            const arr: number[] = [1, 2, 3];
            const push = arr.push;
            push.call(arr, 4);
            console.log(arr.length);
            console.log(arr[3]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n4\n", output);
    }

    [Theory, ModeData]
    public void Array_Push_CallMultipleArgs(ExecutionMode mode)
    {
        var source = @"
            const arr: number[] = [1];
            arr.push.call(arr, 10, 20, 30);
            console.log(arr.length);
            console.log(arr[1]);
            console.log(arr[2]);
            console.log(arr[3]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n10\n20\n30\n", output);
    }

    [Theory, ModeData]
    public void Array_Push_ApplyWithArrayArg(ExecutionMode mode)
    {
        var source = @"
            const arr: number[] = [1, 2, 3];
            arr.push.apply(arr, [5, 6]);
            console.log(arr.length);
            console.log(arr[3]);
            console.log(arr[4]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n5\n6\n", output);
    }

    [Theory, ModeData]
    public void Array_Push_BindThenCall(ExecutionMode mode)
    {
        // bind with receiver + partial args, then invoke with more args.
        var source = @"
            const arr: number[] = [1, 2, 3];
            const pushOnto = arr.push.bind(arr, 100);
            pushOnto(101, 102);
            console.log(arr.length);
            console.log(arr[3]);
            console.log(arr[4]);
            console.log(arr[5]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n100\n101\n102\n", output);
    }

    [Theory, ModeData]
    public void Map_Get_CallAndApply(ExecutionMode mode)
    {
        var source = @"
            const m = new Map<string, number>();
            m.set('a', 1);
            m.set('b', 2);
            const g = m.get;
            console.log(g.call(m, 'a'));
            console.log(g.apply(m, ['b']));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    [Theory, ModeData]
    public void Map_Set_CallChainable(ExecutionMode mode)
    {
        var source = @"
            const m = new Map<string, number>();
            m.set.call(m, 'x', 100);
            m.set.call(m, 'y', 200);
            console.log(m.get('x'));
            console.log(m.get('y'));
            console.log(m.size);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n200\n2\n", output);
    }

    [Theory, ModeData]
    public void Map_Get_BindWithPartialArg(ExecutionMode mode)
    {
        // `.bind(m, 'key')` prepends 'key' so calling with no args invokes get('key').
        var source = @"
            const m = new Map<string, number>();
            m.set('key', 42);
            m.set('other', 99);
            const getKey = m.get.bind(m, 'key');
            const getOther = m.get.bind(m, 'other');
            console.log(getKey());
            console.log(getOther());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n99\n", output);
    }

    [Theory, ModeData]
    public void Set_Has_CallAndApply(ExecutionMode mode)
    {
        var source = @"
            const s = new Set<number>();
            s.add(1);
            s.add(2);
            const h = s.has;
            console.log(h.call(s, 1));
            console.log(h.apply(s, [99]));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory, ModeData]
    public void Set_Add_BindThenCall(ExecutionMode mode)
    {
        var source = @"
            const s = new Set<string>();
            const add = s.add.bind(s);
            add('a');
            add('b');
            console.log(s.size);
            console.log(s.has('a'));
            console.log(s.has('b'));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void BoundMethod_NameProperty(ExecutionMode mode)
    {
        // `.name` on a bound array/map/set method returns the captured method name.
        var source = @"
            const arr: number[] = [];
            const m = new Map<string, number>();
            const s = new Set<number>();
            console.log(arr.push.name);
            console.log(m.get.name);
            console.log(s.has.name);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("push\nget\nhas\n", output);
    }

    [Theory, ModeData]
    public void BoundMethod_TypeofAfterBind(ExecutionMode mode)
    {
        // A bind() result must still report as 'function'.
        var source = @"
            const arr: number[] = [1];
            const bound = arr.push.bind(arr, 42);
            console.log(typeof bound);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\n", output);
    }

    [Theory, ModeData]
    public void CrossModule_MapGet_CallAndApply(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["lib.ts"] = """
                export function makeMap(): unknown {
                    const m = new Map<string, number>();
                    m.set('x', 10);
                    return m;
                }
                """,
            ["main.ts"] = """
                import { makeMap } from './lib';
                const m: any = makeMap();
                const g = m.get;
                console.log(typeof g);
                console.log(g.call(m, 'x'));
                console.log(g.apply(m, ['x']));
                const boundGet = g.bind(m, 'x');
                console.log(boundGet());
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("function\n10\n10\n10\n", output);
    }
}
