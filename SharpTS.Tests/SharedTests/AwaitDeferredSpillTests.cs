using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for #400: a value spilled before an <c>await</c> and used after it
/// must survive the suspension. Unlike <see cref="AwaitStackSpillTests"/> (which awaits a
/// plain value that completes synchronously and so never suspends), every await here is a
/// genuinely <em>deferred</em> promise (resolved from a later event-loop turn via
/// <c>setTimeout</c>), so the compiled state machine actually re-enters MoveNext. Before
/// the fix the prefix/operand spilled to an IL local was lost on re-entry, because only
/// state-machine fields survive a MoveNext re-entry — the compiler printed the suffix only.
/// Runs against both interpreter and compiler.
/// </summary>
public class AwaitDeferredSpillTests
{
    // A promise that settles from a later event-loop turn, forcing the awaiting state
    // machine to actually suspend and resume (rather than complete synchronously).
    private const string Defer =
        "function defer(v: any, ms: number): Promise<any> { return new Promise(r => setTimeout(() => r(v), ms)); }\n";

    private static string Run(string body, ExecutionMode mode)
        => TestHarness.Run(Defer + "class C { x: any = 0; }\nasync function m() {\n" + body + "\n}\nm();\n", mode);

    [Theory, ModeData]
    public void BinaryConcat_PrefixSurvivesDeferredAwait(ExecutionMode mode)
    {
        // The exact shape from the issue: the "concat: " prefix is pushed before the await.
        Assert.Equal("concat: 6\n", Run("""console.log("concat: " + (await defer(6, 5)));""", mode));
    }

    [Theory, ModeData]
    public void BinaryConcat_TwoDeferredAwaits(ExecutionMode mode)
    {
        Assert.Equal("L-R\n", Run("""console.log((await defer("L", 5)) + "-" + (await defer("R", 5)));""", mode));
    }

    [Theory, ModeData]
    public void TemplateLiteral_TwoDeferredAwaits(ExecutionMode mode)
    {
        Assert.Equal("a1-2\n", Run("""console.log(`${"a"}${await defer(1, 5)}-${await defer(2, 5)}`);""", mode));
    }

    [Theory, ModeData]
    public void TaggedTemplate_DeferredAwait(ExecutionMode mode)
    {
        Assert.Equal("p|7\n", Run(
            """
            const tag = (s: any, v: any) => s[0] + "|" + v;
            console.log(tag`p${await defer(7, 5)}`);
            """, mode));
    }

    [Theory, ModeData]
    public void ConsoleLog_MultiArg_DeferredAwaitBetweenArgs(ExecutionMode mode)
    {
        Assert.Equal("A B C\n", Run("""console.log("A", await defer("B", 5), "C");""", mode));
    }

    [Theory, ModeData]
    public void ArrayLiteral_DeferredAwaitBetweenElements(ExecutionMode mode)
    {
        Assert.Equal("[x, 9, y]\n", Run("""console.log(["x", await defer(9, 5), "y"]);""", mode));
    }

    [Theory, ModeData]
    public void ObjectLiteral_DeferredAwaitAfterEarlierProperty(ExecutionMode mode)
    {
        Assert.Equal("""{"a":"x","b":10}""" + "\n", Run(
            """console.log(JSON.stringify({ a: "x", b: await defer(10, 5) }));""", mode));
    }

    [Theory, ModeData]
    public void ComputedKeyObjectLiteral_DeferredAwait(ExecutionMode mode)
    {
        Assert.Equal("""{"dyn":12,"other":99}""" + "\n", Run(
            """
            const k: any = "dyn";
            console.log(JSON.stringify({ [k]: await defer(12, 5), other: 99 }));
            """, mode));
    }

    [Theory, ModeData]
    public void IndexSet_DeferredAwaitInIndexAndValue(ExecutionMode mode)
    {
        Assert.Equal("8\nZ\n", Run(
            """
            const arr: any = [1, 2, 3];
            arr[0] = await defer(8, 5);
            console.log(arr[0]);
            arr[await defer(0, 5)] = "Z";
            console.log(arr[0]);
            """, mode));
    }

    [Theory, ModeData]
    public void CompoundAssign_DeferredAwaitRhs(ExecutionMode mode)
    {
        Assert.Equal("17\n17\n", Run(
            """
            const c = new C();
            c.x = 10;
            c.x += await defer(7, 5);
            console.log(c.x);
            const arr: any = [10];
            arr[0] += await defer(7, 5);
            console.log(arr[0]);
            """, mode));
    }

    [Theory, ModeData]
    public void LogicalAssign_DeferredAwaitRhs(ExecutionMode mode)
    {
        Assert.Equal("11\n11\n", Run(
            """
            const c = new C();
            c.x = null;
            c.x ??= await defer(11, 5);
            console.log(c.x);
            const arr: any = [0];
            arr[0] ||= await defer(11, 5);
            console.log(arr[0]);
            """, mode));
    }

    [Theory, ModeData]
    public void PropertySet_DeferredAwaitValue(ExecutionMode mode)
    {
        Assert.Equal("5\n", Run(
            """
            const o: any = {};
            o.field = await defer(5, 5);
            console.log(o.field);
            """, mode));
    }

    // #413: a top-level function with a rest parameter, whose call args are assembled into an
    // object[]/List<object> on the IL stack. An await inside such an argument must spill the
    // already-evaluated args off the evaluation stack before the array is built — otherwise the
    // array reference sits stacked across the suspension (invalid IL, PathStackDepth) and earlier
    // args are lost on MoveNext re-entry. `g` is all-rest; `h` has a typed regular param first.
    // `h`'s rest-join is hoisted to a local so the body avoids the unrelated #434 BackwardBranch
    // verify bug (`<expr> + rest.join(...)` directly), keeping these tests focused on #413.
    private const string RestScaffold =
        "function g(...a: any[]): string { return a.join(\",\"); }\n" +
        "function h(x: string, ...rest: any[]): string { const s = rest.join(\",\"); return x + \"|\" + s; }\n";

    private static string RunRest(string body, ExecutionMode mode)
        => TestHarness.Run(Defer + RestScaffold + "async function m() {\n" + body + "\n}\nm();\n", mode);

    [Theory, ModeData]
    public void RestParamCall_DeferredAwaitArg(ExecutionMode mode)
    {
        // The exact issue repro shape: await in the second (rest) argument.
        Assert.Equal("x,1\n", RunRest("""console.log(g("x", await defer(1, 5)));""", mode));
    }

    [Theory, ModeData]
    public void RestParamCall_RegularParamSurvivesDeferredAwait(ExecutionMode mode)
    {
        // `h`'s typed regular param "X" is spilled before the deferred await in the rest arg;
        // it must survive the suspension and be coerced back to its declared string slot.
        Assert.Equal("X|y,Z\n", RunRest("""console.log(h("X", "y", await defer("Z", 5)));""", mode));
    }

    [Theory, ModeData]
    public void RestParamCall_TwoDeferredAwaits(ExecutionMode mode)
    {
        Assert.Equal("p,q\n", RunRest("""console.log(g(await defer("p", 5), await defer("q", 5)));""", mode));
    }

    [Theory, ModeData]
    public void RestParamCall_SpreadBetweenDeferredAwaits(ExecutionMode mode)
    {
        // Spread element plus deferred awaits on both sides — exercises the spread/ExpandCallArgs
        // path, which must also read pre-spilled locals rather than re-evaluating across the await.
        Assert.Equal("0,10,20,30\n", RunRest(
            """const arr = [10, 20]; console.log(g(await defer(0, 5), ...arr, await defer(30, 5)));""", mode));
    }

    [Theory, ModeData]
    public void DeferredThenableSpecies_PrefixSurvives(ExecutionMode mode)
    {
        // The original issue repro: a Promise subclass whose @@species is a general
        // (non-Promise) thenable that settles its callback from a later turn (#349/#390).
        var source = """
            class Thenable {
              v: any = undefined;
              settled = false;
              cb: any = undefined;
              constructor(executor: (res: any, rej: any) => void) {
                executor(
                  (x: any) => { this.v = x; this.settled = true; if (this.cb) this.cb(x); },
                  (_e: any) => {});
              }
              then(onF: any) { if (this.settled) onF(this.v); else this.cb = onF; }
            }
            class P extends Promise<number> {
              static get [Symbol.species]() { return Thenable as any; }
            }
            async function main() {
              const r: any = P.resolve(5).then((x: number) => x + 1);
              console.log("concat: " + (await r));
            }
            main();
            """;
        Assert.Equal("concat: 6\n", TestHarness.Run(source, mode));
    }

    // #436: a fixed-arity (non-rest) top-level function call where an earlier argument precedes a
    // deferred await. The direct-call path spilled earlier args to *parameter-typed* IL locals,
    // which do not survive a deferred MoveNext re-entry (only registered field-backed spills do),
    // so the earlier arg read back as null (`nullB`) and the typed reload also failed IL verify.
    // The earlier args are now spilled to registered, boxed locals and coerced back to their
    // declared parameter slot (string / number / boolean) on load.
    private const string FixedArityScaffold =
        "function concat2(x: string, y: string): string { return x + y; }\n" +
        "function add3(a: number, b: number, c: number): number { return a + b + c; }\n" +
        "function pick(flag: boolean, a: string, b: string): string { return flag ? a : b; }\n";

    private static string RunFns(string body, ExecutionMode mode)
        => TestHarness.Run(Defer + FixedArityScaffold + "async function m() {\n" + body + "\n}\nm();\n", mode);

    [Theory, ModeData]
    public void FixedArityCall_EarlierStringArgSurvivesDeferredAwait(ExecutionMode mode)
    {
        // The exact #436 repro shape: f("A", await ...) lost "A" → "nullB".
        Assert.Equal("AB\n", RunFns("""console.log(concat2("A", await defer("B", 5)));""", mode));
    }

    [Theory, ModeData]
    public void FixedArityCall_NumericArgsSurviveDeferredAwait(ExecutionMode mode)
    {
        // Numeric (double) parameters: earlier args boxed across the await, unboxed back on load.
        Assert.Equal("6\n", RunFns("""console.log(add3(1, await defer(2, 5), 3));""", mode));
    }

    [Theory, ModeData]
    public void FixedArityCall_TwoDeferredAwaitArgs(ExecutionMode mode)
    {
        Assert.Equal("12\n", RunFns("""console.log(add3(await defer(3, 5), 4, await defer(5, 5)));""", mode));
    }

    [Theory, ModeData]
    public void FixedArityCall_BooleanArgBeforeDeferredAwait(ExecutionMode mode)
    {
        Assert.Equal("yes\n", RunFns("""console.log(pick(true, await defer("yes", 5), "no"));""", mode));
    }

    // #439: a method call (instance, static, optional-chain) where an earlier argument precedes a
    // deferred await. These dispatch paths emitted the receiver and earlier args onto the IL stack
    // (or into unregistered temps) across the suspension — invalid IL and a runtime crash. The
    // receiver and arguments are now spilled to registered locals and coerced to their slots.
    private const string MethodScaffold =
        "class Calc {\n" +
        "  join(a: string, b: string): string { return a + b; }\n" +
        "  static mul(a: number, b: number): number { return a * b; }\n" +
        "}\n";

    private static string RunMethods(string body, ExecutionMode mode)
        => TestHarness.Run(Defer + MethodScaffold + "async function m() {\n" + body + "\n}\nm();\n", mode);

    [Theory, ModeData]
    public void InstanceMethodCall_EarlierArgSurvivesDeferredAwait(ExecutionMode mode)
    {
        Assert.Equal("AB\n", RunMethods("""const c = new Calc(); console.log(c.join("A", await defer("B", 5)));""", mode));
    }

    [Theory, ModeData]
    public void StaticMethodCall_EarlierArgSurvivesDeferredAwait(ExecutionMode mode)
    {
        Assert.Equal("42\n", RunMethods("""console.log(Calc.mul(6, await defer(7, 5)));""", mode));
    }

    [Theory, ModeData]
    public void OptionalChainMethodCall_EarlierArgSurvivesDeferredAwait(ExecutionMode mode)
    {
        Assert.Equal("AB\n", RunMethods(
            """const o: any = { go(a: string, b: string): string { return a + b; } }; console.log(o?.go("A", await defer("B", 5)));""",
            mode));
    }

    // #614: an await in an argument of a call to a *runtime-dispatchable string method*
    // (substring/substr/charAt/…) on an any-typed receiver. The string fast path
    // (EmitRuntimeStringMethod) and the generic GetProperty path are mutually exclusive at runtime
    // but both contained the arguments at emit time, so each await was emitted twice — desyncing the
    // await-state counter and crashing the compiler with "The given key 'N' was not present in the
    // dictionary." The arguments are now spilled once before the string-vs-generic split.

    [Theory, ModeData]
    public void OptionalChainStringMethod_DeferredAwaitArg(ExecutionMode mode)
    {
        // The exact #614 repro shape: substring (a direct runtime string method) on `any?.`.
        Assert.Equal("ell\n", Run("""const s: any = "hello"; console.log(s?.substring(await defer(1, 5), 4));""", mode));
    }

    [Theory, ModeData]
    public void OptionalChainStringMethod_TwoDeferredAwaits(ExecutionMode mode)
    {
        Assert.Equal("hel\n", Run("""const s: any = "hello"; console.log(s?.substring(await defer(0, 5), await defer(3, 5)));""", mode));
    }

    [Theory, ModeData]
    public void OptionalChainStringMethod_DefaultCasePath_DeferredAwaitArg(ExecutionMode mode)
    {
        // charAt is dispatchable but not one of the direct switch cases, so it routes through the
        // default → GetProperty+Invoke fallback, which must also read the pre-spilled args.
        Assert.Equal("e\n", Run("""const s: any = "hello"; console.log(s?.charAt(await defer(1, 5)));""", mode));
    }

    [Theory, ModeData]
    public void DynamicDispatchStringMethod_DeferredAwaitArg(ExecutionMode mode)
    {
        // Non-optional dynamic dispatch (EmitDynamicMethodCallPreservingThis) has the same
        // dual-emission shape and was the latent sibling gap called out in #614.
        Assert.Equal("ell\n", Run("""const s: any = "hello"; console.log(s.substring(await defer(1, 5), 4));""", mode));
    }

    [Theory, ModeData]
    public void OptionalChainStringMethod_NonStringReceiverWithOwnMethod_DeferredAwaitArg(ExecutionMode mode)
    {
        // Receiver is not a string at runtime, so the generic (non-string) branch runs, reading the
        // same pre-spilled args — the user's own `substring` is invoked, not the string built-in.
        Assert.Equal("OBJ:2,7\n", Run(
            """const o: any = { substring(a: number, b: number): string { return "OBJ:" + a + "," + b; } }; console.log(o?.substring(await defer(2, 5), 7));""",
            mode));
    }

    [Theory, ModeData]
    public void OptionalChainStringMethod_NullishReceiverShortCircuits(ExecutionMode mode)
    {
        Assert.Equal("undefined\n", Run("""const n: any = null; console.log(n?.substring(await defer(1, 5), 4));""", mode));
    }

    // #627: an optional-chain call to a dispatchable-string-method NAME (substring/charAt/…) on a
    // non-string object that LACKS the method, with a deferred-await argument. The chain
    // short-circuits to undefined; the interpreter does so before evaluating the argument, but the
    // compiled await-safe path used to spill the arg before the string-vs-generic split, so the
    // await ran anyway. Both modes must now leave the argument unevaluated (parity). The result is
    // undefined in both either way — only the side effect differs.
    // Observes whether an optional-chain call's awaited argument runs. `ran` and the synchronous
    // flag-setter `mark()` live at top level (compiled mode supports neither a nested function nor a
    // nested async function inside the async body); the awaited arg folds in `mark()` so its side
    // effect fires iff the argument is evaluated. If the chain short-circuits first, `ran` stays false.
    private static string RunOptionalChainObserve(string receiverDecl, string call, ExecutionMode mode)
        => TestHarness.Run(
            Defer +
            "let ran = false;\n" +
            "function mark(): number { ran = true; return 1; }\n" +
            receiverDecl + "\n" +
            "async function m() { const r = " + call + "; console.log(\"r=\" + r + \" ran=\" + ran); }\n" +
            "m();\n",
            mode);

    [Theory, ModeData]
    public void OptionalChainStringMethod_MissingOnNonStringReceiver_DoesNotEvaluateAwaitArg(ExecutionMode mode)
    {
        // The #627 repro: substring on a non-string object that lacks it. Both modes short-circuit to
        // undefined WITHOUT running the awaited arg (compiled used to spill it before the split).
        Assert.Equal("r=undefined ran=false\n", RunOptionalChainObserve(
            "const o: any = { foo: 1 };", "o?.substring(mark() + (await defer(0, 5)), 4)", mode));
    }

    [Theory, ModeData]
    public void OptionalChainNonStringMethod_MissingOnReceiver_DoesNotEvaluateAwaitArg(ExecutionMode mode)
    {
        // The non-dispatchable-name sibling (slice) was already consistent — it never had a string
        // fast path, so its arg spill already happened after the fn-nullish short-circuit. Pinned so
        // the two stay aligned.
        Assert.Equal("r=undefined ran=false\n", RunOptionalChainObserve(
            "const o: any = { foo: 1 };", "o?.slice(mark() + (await defer(0, 5)), 4)", mode));
    }

    [Theory, ModeData]
    public void OptionalChainStringMethod_StringReceiver_EvaluatesAwaitArg(ExecutionMode mode)
    {
        // A string receiver DOES have the method, so the argument must evaluate (side effect runs) and
        // the real result comes back — the deferred-await arg still survives the suspension.
        Assert.Equal("r=ell ran=true\n", RunOptionalChainObserve(
            "const s: any = \"hello\";", "s?.substring(mark() + (await defer(0, 5)), 4)", mode));
    }
}
