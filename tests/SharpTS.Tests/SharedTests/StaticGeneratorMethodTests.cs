using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for static generator methods — `class C { static *gen() { yield … } }` (#692) and the async
/// form `static async *gen()` (#778). The instance forms already compiled; this pins the static forms,
/// whose state machines are set up like a free function (no `this`). Runs in both back ends.
/// </summary>
public class StaticGeneratorMethodTests
{
    [Theory, ModeData]
    public void StaticGenerator_SingleYield_Works(ExecutionMode mode)
    {
        var source = """
            class C { static *gen() { yield 7; } }
            console.log([...C.gen()][0]);
            """;

        Assert.Equal("7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StaticGenerator_WithParameter_Works(ExecutionMode mode)
    {
        var source = """
            class C { static *range(n: number) { for (let i = 0; i < n; i++) yield i * 2; } }
            console.log([...C.range(3)].join(","));
            for (const x of C.range(2)) console.log(x);
            """;

        Assert.Equal("0,2,4\n0\n2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StaticGenerator_ExtraArguments_AreEvaluatedAndIgnored(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                let effects = 0;
                class C { static *gen(value: number) { yield value; } }
                console.log([...C.gen(7, effects++, "ignored")].join(","));
                console.log(effects);
                """,
        };

        Assert.Equal(
            "7\n1\n",
            TestHarness.RunModules(files, "main.ts", mode, allowTypeErrors: true));
    }

    [Theory, ModeData]
    public void StaticGenerator_RestParameterCollectsExtraArguments(ExecutionMode mode)
    {
        var source = """
            class C {
              static *gen(first: number, ...rest: any[]) {
                yield first;
                yield rest.join(",");
              }
            }
            console.log([...C.gen(1, 2, 3)].join("|"));
            """;

        Assert.Equal("1|2,3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StaticGenerator_ExtraArgumentsThroughStaticThis_AreIgnored(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                let effects = 0;
                class C {
                  static *gen(value: number) { yield value; }
                  static run() { return [...this.gen(4, effects++)].join(","); }
                }
                console.log(C.run());
                console.log(effects);
                """,
        };

        Assert.Equal(
            "4\n1\n",
            TestHarness.RunModules(files, "main.ts", mode, allowTypeErrors: true));
    }

    [Theory, ModeData]
    public void ImportedStaticGenerator_ExtraArguments_AreIgnored(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["lib.ts"] = """
                export class C { static *gen(value: number) { yield value; } }
                """,
            ["main.ts"] = """
                import { C } from "./lib";
                let effects = 0;
                console.log([...C.gen(6, effects++)].join(","));
                console.log(effects);
                """,
        };

        Assert.Equal(
            "6\n1\n",
            TestHarness.RunModules(files, "main.ts", mode, allowTypeErrors: true));
    }

    [Theory, ModeData]
    public void StaticGenerator_ReadsStaticField_Works(ExecutionMode mode)
    {
        var source = """
            class C {
              static count = 3;
              static *fromField() { for (let i = 0; i < C.count; i++) yield i; }
            }
            console.log([...C.fromField()].join(","));
            """;

        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StaticGenerator_ExplicitReturnType_Works(ExecutionMode mode)
    {
        // Per #692: an explicit Generator<T> return type doesn't change the lowering path.
        var source = """
            class C { static *gen(): Generator<number> { yield 1; yield 2; yield 3; } }
            console.log([...C.gen()].length);
            """;

        Assert.Equal("3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InstanceGenerator_StillWorks(ExecutionMode mode)
    {
        // Regression guard: the instance form (the path #692 did not touch) keeps working.
        var source = """
            class C { *gen() { yield 7; } }
            console.log([...new C().gen()][0]);
            """;

        Assert.Equal("7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StaticAsyncGenerator_ForAwait_Works(ExecutionMode mode)
    {
        // #778: a static async generator (static async *m()) was emitted as a plain async method, so
        // its `yield` hit "Yield not supported in this context". It now routes through the async
        // generator state machine with a static stub (no `this`), like a free async generator.
        var source = """
            class C { static async *gen() { yield 1; yield 2; } }
            async function main() { for await (const x of C.gen()) console.log(x); }
            main();
            """;

        Assert.Equal("1\n2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StaticAsyncGenerator_ParameterAndAwait_Works(ExecutionMode mode)
    {
        // Reads a parameter (in a yield, not a for-condition — see the CompiledOnly test below) and
        // suspends on a real await between yields.
        var source = """
            class C { static async *gen(a: number) { yield a; await Promise.resolve(0); yield a * 2; } }
            async function main() { for await (const x of C.gen(5)) console.log(x); }
            main();
            """;

        Assert.Equal("5\n10\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StaticAsyncGenerator_ExtraArguments_AreEvaluatedAndIgnored(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                let effects = 0;
                class C { static async *gen(value: number) { yield value; } }
                async function extra() { effects++; return "ignored"; }
                async function main() {
                  for await (const value of C.gen(9, await extra(), "ignored")) console.log(value);
                  console.log(effects);
                }
                main();
                """,
        };

        Assert.Equal(
            "9\n1\n",
            TestHarness.RunModules(files, "main.ts", mode, allowTypeErrors: true));
    }

    [Theory, ModeData]
    public void StaticAsyncGenerator_DefaultParamLoopAndAwait_Works(ExecutionMode mode)
    {
        // Exercises a default value-type parameter, a C-style for loop whose bound reads that
        // parameter, and an await inside the loop — the full #778 shape (the generator
        // default-parameter prologue from #737 applies for free).
        var source = """
            class C { static async *gen(n: number = 3): AsyncGenerator<number> {
              for (let i = 0; i < n; i++) { await Promise.resolve(0); yield i * 10; }
            } }
            async function main() {
              for await (const x of C.gen()) console.log(x);
              for await (const x of C.gen(2)) console.log("g:" + x);
            }
            main();
            """;

        Assert.Equal("0\n10\n20\ng:0\ng:10\n", TestHarness.Run(source, mode));
    }
}
