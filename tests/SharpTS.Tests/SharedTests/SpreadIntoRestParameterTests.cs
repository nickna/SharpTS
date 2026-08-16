using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Interpreter↔compiled parity for spread arguments landing in a rest parameter,
/// across every callable shape that can host one.
/// </summary>
/// <remarks>
/// <para>
/// Compiled mode had two independent defects here (#1282), both silent rather than
/// loud, and neither visible to a single-mode test:
/// </para>
/// <list type="bullet">
/// <item>The dynamic method-call path built its argument array without expanding
/// <c>...xs</c>, so the iterable arrived as one argument and settled into the rest
/// parameter as a single nested element — <c>o.m(...['a','b'])</c> saw
/// <c>[['a','b']]</c>.</item>
/// <item>Rest-ness is recognized only by a trailing <c>List&lt;object&gt;</c> CLR
/// slot, and the parameter resolvers pinned that type on just one of their paths.
/// Class methods and constructors never did, so a rest parameter collected only its
/// first argument — whether it worked depended on if the declared element type
/// happened to map onto the marker (<c>...p: string[]</c> did, <c>...p: any[]</c>
/// did not).</item>
/// </list>
/// <para>
/// Cases assert the received <em>arity and contents</em> via JSON rather than a
/// derived string: a length-1 nested array stringifies close enough to the right
/// answer to slip past a looser assertion.
/// </para>
/// </remarks>
public class SpreadIntoRestParameterTests
{
    private static void AssertLines(string source, string expected, ExecutionMode mode)
    {
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal(Normalize(expected), Normalize(output));
    }

    private static string Normalize(string s) =>
        string.Join("\n", s.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd())).Trim();

    /// <summary>
    /// Every callable shape that can declare a rest parameter, each called three ways:
    /// plain args, a bare spread, and a leading arg followed by a spread.
    /// </summary>
    [Theory, ModeData]
    public void SpreadIntoRest_AllCallableShapes(ExecutionMode mode)
    {
        AssertLines("""
            const args: string[] = ['a', 'b'];

            const objMethod = { m(...p: any[]): any[] { return p; } };
            const objArrow = { m: (...p: any[]): any[] => p };
            const objFnExpr = { m: function (...p: any[]): any[] { return p; } };
            const nested = { inner: { m(...p: any[]): any[] { return p; } } };
            class K { m(...p: any[]): any[] { return p; } }
            const k = new K();
            function plain(...p: any[]): any[] { return p; }

            console.log('objMethod ' + JSON.stringify([objMethod.m('a', 'b'), objMethod.m(...args), objMethod.m('z', ...args)]));
            console.log('objArrow  ' + JSON.stringify([objArrow.m('a', 'b'), objArrow.m(...args), objArrow.m('z', ...args)]));
            console.log('objFnExpr ' + JSON.stringify([objFnExpr.m('a', 'b'), objFnExpr.m(...args), objFnExpr.m('z', ...args)]));
            console.log('nested    ' + JSON.stringify([nested.inner.m('a', 'b'), nested.inner.m(...args), nested.inner.m('z', ...args)]));
            console.log('class     ' + JSON.stringify([k.m('a', 'b'), k.m(...args), k.m('z', ...args)]));
            console.log('plain     ' + JSON.stringify([plain('a', 'b'), plain(...args), plain('z', ...args)]));
            """,
            """
            objMethod [["a","b"],["a","b"],["z","a","b"]]
            objArrow  [["a","b"],["a","b"],["z","a","b"]]
            objFnExpr [["a","b"],["a","b"],["z","a","b"]]
            nested    [["a","b"],["a","b"],["z","a","b"]]
            class     [["a","b"],["a","b"],["z","a","b"]]
            plain     [["a","b"],["a","b"],["z","a","b"]]
            """, mode);
    }

    /// <summary>
    /// The rest parameter's declared element type must not change dispatch. Before the
    /// fix <c>...p: string[]</c> worked by accident and <c>...p: any[]</c> did not.
    /// </summary>
    [Theory, ModeData]
    public void SpreadIntoRest_ElementTypeDoesNotChangeDispatch(ExecutionMode mode)
    {
        AssertLines("""
            const args: string[] = ['a', 'b'];
            const nums: number[] = [1, 2];

            class Typed { m(...p: string[]): any[] { return p; } }
            class Anys { m(...p: any[]): any[] { return p; } }
            class Untyped { m(...p): any[] { return p; } }
            class Nums { m(...p: number[]): any[] { return p; } }

            console.log('string[] ' + JSON.stringify([new Typed().m('a', 'b'), new Typed().m(...args)]));
            console.log('any[]    ' + JSON.stringify([new Anys().m('a', 'b'), new Anys().m(...args)]));
            console.log('untyped  ' + JSON.stringify([new Untyped().m('a', 'b'), new Untyped().m(...args)]));
            console.log('number[] ' + JSON.stringify([new Nums().m(1, 2), new Nums().m(...nums)]));
            """,
            """
            string[] [["a","b"],["a","b"]]
            any[]    [["a","b"],["a","b"]]
            untyped  [["a","b"],["a","b"]]
            number[] [[1,2],[1,2]]
            """, mode);
    }

    /// <summary>
    /// Leading (non-rest) parameters must still receive their own values when a spread
    /// follows, an empty spread must contribute nothing, and a spread may also supply
    /// the leading parameters.
    /// </summary>
    [Theory, ModeData]
    public void SpreadIntoRest_RegularParamsAndEmptySpread(ExecutionMode mode)
    {
        AssertLines("""
            const args: string[] = ['a', 'b'];
            const empty: string[] = [];
            const three: string[] = ['x', 'y', 'a'];

            class C { m(first: string, second: string, ...p: any[]): any[] { return [first, second, p]; } }
            const c = new C();
            const o = { m(first: string, ...p: any[]): any[] { return [first, p]; } };

            console.log('cls-lead  ' + JSON.stringify(c.m('x', 'y', ...args)));
            console.log('cls-fills ' + JSON.stringify(c.m(...three)));
            console.log('cls-empty ' + JSON.stringify(c.m('x', 'y', ...empty)));
            console.log('obj-lead  ' + JSON.stringify(o.m('x', ...args)));
            console.log('obj-empty ' + JSON.stringify(o.m('x', ...empty)));
            console.log('obj-two   ' + JSON.stringify(o.m('x', ...args, ...args)));
            """,
            """
            cls-lead  ["x","y",["a","b"]]
            cls-fills ["x","y",["a"]]
            cls-empty ["x","y",[]]
            obj-lead  ["x",["a","b"]]
            obj-empty ["x",[]]
            obj-two   ["x",["a","b","a","b"]]
            """, mode);
    }

    /// <summary>
    /// A spread argument only has to be iterable, not an array. The runtime expander walks
    /// Symbol.iterator, so every iterable kind must both type-check and expand — in both
    /// modes. The type checker previously demanded an array outright (TS2488), which also
    /// rejected tuples, and typed arrays/Buffers were missing from the interpreter's
    /// iterable switch even though the compiled path expanded them.
    /// </summary>
    [Theory, ModeData]
    public void SpreadIntoRest_AcceptsEveryIterableKind(ExecutionMode mode)
    {
        AssertLines("""
            function collect(...p: any[]): string { return JSON.stringify(p); }
            const o = { m(...p: any[]): string { return JSON.stringify(p); } };
            class K { m(...p: any[]): string { return JSON.stringify(p); } }
            function* gen(): Generator<string> { yield 'a'; yield 'b'; }

            const arr: string[] = ['a', 'b'];
            const tup: [string, number] = ['a', 1];
            const set = new Set<string>(['a', 'b']);
            const map = new Map<string, number>([['a', 1]]);
            const u8 = new Uint8Array([1, 2]);
            const buf = Buffer.from([3, 4]);

            console.log('array     ' + collect(...arr));
            console.log('tuple     ' + collect(...tup));
            console.log('set       ' + collect(...set));
            console.log('setValues ' + collect(...set.values()));
            console.log('map       ' + collect(...map));
            console.log('string    ' + collect(...'ab'));
            console.log('generator ' + collect(...gen()));
            console.log('typedarray ' + collect(...u8));
            console.log('buffer    ' + collect(...buf));
            console.log('objMethod ' + o.m(...set));
            console.log('classMeth ' + new K().m(...gen()));
            console.log('mixed     ' + collect('z', ...set));
            """,
            """
            array     ["a","b"]
            tuple     ["a",1]
            set       ["a","b"]
            setValues ["a","b"]
            map       [["a",1]]
            string    ["a","b"]
            generator ["a","b"]
            typedarray [1,2]
            buffer    [3,4]
            objMethod ["a","b"]
            classMeth ["a","b"]
            mixed     ["z","a","b"]
            """, mode);
    }

    /// <summary>
    /// A genuinely non-iterable spread operand must still be rejected — the fix widened the
    /// check to "iterable", not "anything".
    /// </summary>
    [Theory, ModeData]
    public void SpreadIntoRest_RejectsNonIterable(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                function collect(...p: any[]): string { return JSON.stringify(p); }
                const n: number = 5;
                console.log(collect(...n));
                """
        };

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunModules(files, "main.ts", mode));
        Assert.Contains("iterable", ex.Message);
    }

    /// <summary>
    /// The async/state-machine emitters reach rest packing through their own argument
    /// spilling (an <c>await</c> in an argument must not strand a partly-built array on
    /// the IL stack), so they get their own coverage — including a spread that follows
    /// an awaited leading argument.
    /// </summary>
    [Theory, ModeData]
    public void SpreadIntoRest_InsideAsyncBody(ExecutionMode mode)
    {
        AssertLines("""
            const args: string[] = ['a', 'b'];
            const o = { m(...p: any[]): any[] { return p; } };
            class K { m(...p: any[]): any[] { return p; } }
            async function delay(v: string): Promise<string> { return v; }

            async function main(): Promise<void> {
                console.log('obj        ' + JSON.stringify(o.m(...args)));
                console.log('class      ' + JSON.stringify(new K().m(...args)));
                const first = await delay('z');
                console.log('mixed      ' + JSON.stringify(o.m(first, ...args)));
                console.log('await-arg  ' + JSON.stringify(o.m(await delay('q'), ...args)));
            }

            main();
            """,
            """
            obj        ["a","b"]
            class      ["a","b"]
            mixed      ["z","a","b"]
            await-arg  ["q","a","b"]
            """, mode);
    }

    /// <summary>
    /// The concrete regression that surfaced this: <c>path.win32.join(...)</c> is a
    /// stdlib object-literal namespace method with a rest parameter, and the spread
    /// form crashed compiled output with an InvalidCastException.
    /// </summary>
    [Theory, ModeData]
    public void SpreadIntoRest_StdlibNamespaceMethod(ExecutionMode mode)
    {
        AssertLines("""
            import * as path from 'path';
            const parts: string[] = ['C:/a', 'b'];
            console.log(path.win32.join(...parts));
            console.log(path.posix.join('/a', 'b'));
            console.log(path.posix.join(...['/a', 'b']));
            """,
            """
            C:\a\b
            /a/b
            /a/b
            """, mode);
    }
}
