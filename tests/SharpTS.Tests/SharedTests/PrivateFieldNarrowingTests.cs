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
}
