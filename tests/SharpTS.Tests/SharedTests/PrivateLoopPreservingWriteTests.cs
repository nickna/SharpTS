using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class PrivateLoopPreservingWriteTests
{
    public static string[] PreservingStringLoops =>
    [
        "for (let i = 0; i < 2; i++) { READ WRITE }",
        "let i = 0; while (i++ < 2) { READ WRITE }",
        "let i = 0; do { READ WRITE } while (i++ < 1);",
        "for (const entry of [1, 2]) { READ WRITE }",
        "for (const entry in { a: 1, b: 2 }) { READ WRITE }",
        "for (let i = 0; i < 2; this.#holder.#value = 'again') { READ i++; }"
    ];

    public static IEnumerable<object[]> StringModeCases() => PreservingStringLoops
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { mode, row }));

    [Theory, MemberData(nameof(StringModeCases))]
    public void NestedStringWritesPreserveTheOuterGuard(ExecutionMode mode, string loop)
    {
        string source = StringSource(loop.Replace("READ", "const value: string = this.#holder.#value; console.log(value);")
            .Replace("WRITE", "this.#holder.#value = 'again';"));
        Assert.Equal("safe\nagain\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NestedNumericUpdatesPreserveTheOuterGuard(ExecutionMode mode)
    {
        const string source = """
            class C {
                #holder: C = this;
                counter: number | undefined = 1;
                run() {
                    if (this.#holder.counter !== undefined) {
                        for (let i = 0; i < 2; i++) {
                            const value: number = this.#holder.counter;
                            console.log(value);
                            this.#holder.counter++;
                        }
                        for (let i = 0; i < 2; --this.#holder.counter) {
                            const value: number = this.#holder.counter;
                            console.log(value);
                            i++;
                        }
                    }
                }
            }
            new C().run();
            """;
        Assert.Equal("1\n2\n3\n2\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void EveryWriteMustPreserveTheGuardedType()
    {
        foreach (string write in new[]
        {
            "this.#holder.#value = 'again'; this.#holder.#value = undefined;",
            "this.#holder.#value = 1;",
            "this.#holder.#value = next();",
            "this.#holder = new C();"
        })
        {
            string source = StringSource("for (let i = 0; i < 2; i++) { " +
                "const value: string = this.#holder.#value; " + write + " }");
            Assert.Contains(Check(source).Diagnostics, diagnostic => diagnostic.TsCode == "TS2322");
        }
    }

    [Fact]
    public void UnsafeIncrementClauseStillInvalidatesTheGuard()
    {
        string source = StringSource("for (let i = 0; i < 2; this.#holder.#value = undefined) { " +
            "const value: string = this.#holder.#value; i++; }");
        Assert.Contains(Check(source).Diagnostics, diagnostic => diagnostic.TsCode == "TS2322");
    }

    [Fact]
    public void UnanalysedWriteContainersDoNotRelaxInvalidation()
    {
        const string source = """
            class C {
                #holder: C = this;
                #value: string | undefined = 'safe';
                #accept(value: unknown) {}
                run() {
                    if (this.#holder.#value) {
                        for (let i = 0; i < 2; i++) {
                            const value: string = this.#holder.#value;
                            this.#holder.#value = 'again';
                            this.#accept(this.#holder.#value = undefined);
                        }
                    }
                }
            }
            """;
        Assert.Contains(Check(source).Diagnostics, diagnostic => diagnostic.TsCode == "TS2322");
    }

    [Fact]
    public void NumericUpdateDoesNotPreserveALiteralGuard()
    {
        const string source = """
            class C {
                #holder: C = this;
                counter: 1 | 2 | undefined = 1;
                run() {
                    if (this.#holder.counter === 1) {
                        for (let i = 0; i < 2; i++) {
                            const value: 1 = this.#holder.counter;
                            this.#holder.counter++;
                        }
                    }
                }
            }
            """;
        Assert.Contains(Check(source).Diagnostics, diagnostic => diagnostic.TsCode == "TS2322");
    }

    [Fact]
    public void EnclosingPrivateOwnerGuardSurvivesInnerOwnerWrites()
    {
        const string source = """
            class Outer {
                #value: string | undefined = 'safe';
                make() {
                    const outer = this;
                    return class Inner {
                        #counter: number = 0;
                        run() {
                            if (outer.#value) {
                                for (let i = 0; i < 2; i++) {
                                    const value: string = outer.#value;
                                    this.#counter += 1;
                                }
                            }
                        }
                    };
                }
            }
            """;
        Assert.Empty(Check(source).Diagnostics);
    }

    private static string StringSource(string loop) => """
        function next(): string | number | undefined { return undefined; }
        class C {
            #holder: C = this;
            #value: string | number | undefined = 'safe';
            run() { if (typeof this.#holder.#value === 'string') { LOOP } }
        }
        new C().run();
        """.Replace("LOOP", loop);

    private static SharpTS.Diagnostics.TypeCheckDiagnosticResult Check(string source) => new TypeChecker()
        .CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
}
