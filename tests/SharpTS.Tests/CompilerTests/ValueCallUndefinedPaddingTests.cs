using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// #640: in compiled mode, invoking a user function as a value (cross-module imports, callbacks,
/// <c>$TSFunction.Invoke</c>) must pad omitted trailing optional arguments with the <c>undefined</c>
/// sentinel — not CLR null — so <c>typeof</c>, <c>=== undefined</c>, and <c>=== null</c> answer
/// correctly. Runtime built-ins keep null padding (they use null-checks for optional-arg absence).
/// Both modes are pinned for interpreter/compiler parity; the compiled path was the defective one.
/// </summary>
public class ValueCallUndefinedPaddingTests
{
    [Theory, ModeData]
    public void CrossModuleImport_OmittedOptionalArg_IsUndefined(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["helper.ts"] = """
                export function h(x?: any): string { return typeof x; }
                export function h2(x?: any): boolean { return x === undefined; }
                export function h3(x?: any): boolean { return x === null; }
                """,
            ["main.ts"] = """
                import { h, h2, h3 } from './helper';
                console.log(h());
                console.log(h2());
                console.log(h3());
                """,
        };
        Assert.Equal("undefined\ntrue\nfalse\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void FunctionDeclarationAsValue_OmittedOptionalArg_IsUndefined(ExecutionMode mode)
    {
        var source = """
            function fdecl(x?: any): string { return typeof x; }
            const r = fdecl;
            console.log(r());
            """;
        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ArrowAsValue_OmittedOptionalArg_IsUndefined(ExecutionMode mode)
    {
        var source = """
            const farrow = (x?: any): string => typeof x;
            console.log(farrow());
            """;
        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Callback_OmittedOptionalArg_IsUndefined(ExecutionMode mode)
    {
        var source = """
            function callIt(cb: (x?: any) => string): string { return cb(); }
            console.log(callIt((x?: any) => typeof x));
            """;
        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InnerFunctionAsValue_OmittedOptionalArg_IsUndefined(ExecutionMode mode)
    {
        var source = """
            function outer(): string {
              function inner(x?: any): string { return typeof x; }
              const r = inner;
              return r();
            }
            console.log(outer());
            """;
        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void BuiltInCallback_OmittedTrailingArg_StillNullPadded(ExecutionMode mode)
    {
        // Regression guard: built-in array callbacks pad missing trailing slots with null
        // (not the sentinel). An arrow callback only declaring the element parameter must still
        // work — and a `function` callback observing arity sees the standard 3 args.
        var source = """
            const doubled = [1, 2, 3].map(x => x * 2);
            console.log(doubled.join(","));
            """;
        Assert.Equal("2,4,6\n", TestHarness.Run(source, mode));
    }

    // #703: class methods invoked as values (extracted, `.bind()`-ed, passed as callbacks →
    // `$TSFunction.Invoke`) must also pad omitted trailing optional args with the `undefined`
    // sentinel, matching function declarations/arrows. Marking is applied to every user
    // class-method builder kind (instance, static, private, class-expression).

    [Theory, ModeData]
    public void InstanceMethodBound_OmittedOptionalArg_IsUndefined(ExecutionMode mode)
    {
        var source = """
            class C { m(x?: any): string { return typeof x; } }
            const inst = new C();
            const mval = inst.m.bind(inst);
            console.log(mval());
            """;
        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InstanceMethodAsValue_OmittedOptionalArg_StrictEquality(ExecutionMode mode)
    {
        var source = """
            class C {
              isUndef(x?: any): boolean { return x === undefined; }
              isNull(x?: any): boolean { return x === null; }
            }
            const c = new C();
            const u = c.isUndef.bind(c);
            const n = c.isNull.bind(c);
            console.log(u());
            console.log(n());
            """;
        Assert.Equal("true\nfalse\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StaticMethodAsValue_OmittedOptionalArg_IsUndefined(ExecutionMode mode)
    {
        var source = """
            class C { static m(x?: any): string { return typeof x; } }
            const f = C.m;
            console.log(f());
            """;
        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ClassExpressionMethodAsValue_OmittedOptionalArg_IsUndefined(ExecutionMode mode)
    {
        var source = """
            const C = class { m(x?: any): string { return typeof x; } };
            const c = new C();
            const mval = c.m.bind(c);
            console.log(mval());
            """;
        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InstanceMethodAsCallback_OmittedOptionalArg_IsUndefined(ExecutionMode mode)
    {
        var source = """
            class C { m(x?: any): string { return typeof x; } }
            function invoke(cb: () => string): string { return cb(); }
            const c = new C();
            console.log(invoke(c.m.bind(c)));
            """;
        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    // #925: free-function declarations that compile to a state-machine stub — `async function`,
    // `function*`, and `async function*` — were never marked $PadUndefined (only their class-method
    // and async-arrow counterparts were), so an omitted optional reference arg padded CLR null
    // instead of the sentinel on the value-call / cross-module boundary. `typeof`/`=== undefined`
    // then answered "object"/false. Marking the stub fixes all three kinds.

    [Theory, ModeData]
    public void CrossModuleImport_AsyncFunction_OmittedOptionalArg_IsUndefined(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["helper.ts"] = """
                export async function h(x?: any): Promise<string> { return typeof x; }
                export async function h2(x?: any): Promise<boolean> { return x === undefined; }
                """,
            ["main.ts"] = """
                import { h, h2 } from './helper';
                async function main() {
                    console.log(await h());
                    console.log(await h2());
                }
                main();
                """,
        };
        Assert.Equal("undefined\ntrue\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void CrossModuleImport_Generator_OmittedOptionalArg_IsUndefined(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["helper.ts"] = """
                export function* gen(x?: any): Generator<string> { yield typeof x; }
                """,
            ["main.ts"] = """
                import { gen } from './helper';
                console.log(gen().next().value);
                """,
        };
        Assert.Equal("undefined\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void CrossModuleImport_AsyncGenerator_OmittedOptionalArg_IsUndefined(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["helper.ts"] = """
                export async function* agen(x?: any): AsyncGenerator<string> { yield typeof x; }
                """,
            ["main.ts"] = """
                import { agen } from './helper';
                async function consume(): Promise<void> {
                    for await (const v of agen()) console.log(v);
                }
                consume();
                """,
        };
        Assert.Equal("undefined\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void AsyncFunctionAsValue_OmittedOptionalArg_IsUndefined(ExecutionMode mode)
    {
        // Value-call (non-module) path for the async free-function stub.
        var source = """
            async function h(x?: any): Promise<string> { return typeof x; }
            const r: any = h;
            r().then((v: string) => console.log(v));
            """;
        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }
}
