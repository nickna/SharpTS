using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class PrivateFieldNarrowingTests
{
    [Theory, ModeData]
    public void PrivateFieldGuardsNarrowTheSameLexicalReference(ExecutionMode mode)
    {
        const string source = """
            class C {
                #value: string | undefined = "ok";
                run() {
                    if (this.#value) console.log(this.#value.length);
                    if (this.#value !== undefined) console.log(this.#value.length);
                    if (typeof this.#value === "string") console.log(this.#value.length);
                }
            }
            new C().run();
            """;
        Assert.Equal("2\n2\n2\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [InlineData("this.#value = undefined; console.log(this.#value.length);")]
    [InlineData("console.log(other.#value.length);")]
    public void PrivateFieldNarrowingDoesNotSurviveWritesOrOtherReceivers(string body)
    {
        string source = "class C { #value: string | undefined = \"ok\"; " +
            "run(other: C) { if (this.#value) { " + body + " } } }";
        Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
    }

    [Fact]
    public void SameSpelledPrivateFieldsHaveDifferentNarrowingPaths()
    {
        const string source = """
            class Outer {
                #value: string | undefined = "ok";
                run() {
                    if (this.#value) {
                        class Inner {
                            #value: number | undefined = undefined;
                            run() { const invalid: number = this.#value; }
                        }
                    }
                }
            }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
    }

    [Theory]
    [InlineData("for (let i = 0; i < 2; i++) { READ WRITE }")]
    [InlineData("let i = 0; while (i++ < 2) { READ WRITE }")]
    [InlineData("let i = 0; do { READ WRITE } while (i++ < 1);")]
    [InlineData("for (const entry of [1, 2]) { READ WRITE }")]
    [InlineData("for (const entry in { a: 1, b: 2 }) { READ WRITE }")]
    [InlineData("for (let i = 0; i < 2; this.#holder.#value = undefined) { READ i++; }")]
    public void NestedPrivateWritesInvalidateOuterGuardsBeforeLoopReentry(string loop)
    {
        string source = """
            class C {
                #holder: C = this;
                #value: string | undefined = 'ok';
                run() { if (this.#holder.#value) { LOOP } }
            }
            """
            .Replace("LOOP", loop)
            .Replace("READ", "const value: string = this.#holder.#value;")
            .Replace("WRITE", "this.#holder.#value = undefined;");
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal("TS2322", error.Diagnostic.TsCode);
    }

    [Theory]
    [InlineData("this.#holder.#value", "this.#holder.#value &&= undefined;")]
    [InlineData("this.#holder.value", "this.#holder.value = undefined;")]
    [InlineData("this.#holder.#tuple[0]", "this.#holder.#tuple[0] = undefined;")]
    public void NestedPrivateReceiversDoNotHideOtherLoopWriteForms(string target, string write)
    {
        string source = """
            class C {
                #holder: C = this;
                #value: string | undefined = 'ok';
                value: string | undefined = 'ok';
                #tuple: [string | undefined] = ['ok'];
                run() {
                    if (TARGET) {
                        for (let i = 0; i < 2; i++) {
                            const value: string = TARGET;
                            WRITE
                        }
                    }
                }
            }
            """.Replace("TARGET", target).Replace("WRITE", write);
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal("TS2322", error.Diagnostic.TsCode);
    }

    [Theory, ModeData]
    public void NestedPrivateLoopWritesPreserveSiblingAndRenewedGuards(ExecutionMode mode)
    {
        const string source = """
            class C {
                #holder: C = this;
                #value: string | undefined = 'ok';
                #stable: string | undefined = 'safe';
                run() {
                    if (this.#holder.#stable) {
                        for (let i = 0; i < 2; i++) {
                            const stable: string = this.#holder.#stable;
                            console.log(stable);
                            this.#holder.#value = undefined;
                        }
                    }
                    this.#holder.#value = 'again';
                    while (this.#holder.#value) {
                        console.log(this.#holder.#value.length);
                        this.#holder.#value = undefined;
                    }
                    this.#holder.#value = 'once';
                    for (let i = 0; i < 2; i++) {
                        if (this.#holder.#value) {
                            console.log(this.#holder.#value.length);
                            this.#holder.#value = undefined;
                        }
                    }
                }
            }
            new C().run();
            """;
        Assert.Equal("safe\nsafe\n5\n4\n", TestHarness.Run(source, mode));
    }
}
