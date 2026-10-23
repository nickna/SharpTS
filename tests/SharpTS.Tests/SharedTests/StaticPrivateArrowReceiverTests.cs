using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class StaticPrivateArrowReceiverTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["instance-private-field-static-arrow", """
            class Owner { #instance=3; static #secret=7; static make(){const read=()=>this.#secret;return read();} } console.log(Owner.make());
            """, "7\n"];
        yield return ["body-this-arrow-no-default", """
            class Owner {
                static #secret = 7;
                static make() { const read = () => this.#secret; return read(); }
            }
            console.log(Owner.make());
            """, "7\n"];
        yield return ["instance-private-field-static-identity", """
            class Owner { #instance=3; static #secret=7; static make(){const read=()=>this;return read();} } console.log(Owner.make()===Owner);
            """, "true\n"];
        yield return ["default-class-name-arrow", """
            class Owner {
                static #secret = 7;
                static make(read: any = () => Owner.#secret) { return read(); }
            }
            console.log(Owner.make());
            """, "7\n"];
        yield return ["subclass-inherited-static-arrow-brand", """
            class Owner {
                #instance = 3;
                static #secret = 7;
                static make() {
                    const read = () => this.#secret;
                    return read();
                }
            }
            class Child extends Owner {}
            try {
                console.log(Child.make());
            } catch (error: any) {
                console.log(error.name);
            }
            """, "TypeError\n"];
        yield return ["instance-private-arrow-preservation", """
            class Owner {
                #instance = 3;
                static #secret = 7;
                make() {
                    const read = () => this.#instance;
                    return read();
                }
            }
            console.log(new Owner().make());
            """, "3\n"];
        yield return ["static-private-field-before-install", """
            try {
                class Owner {
                    #instance = 3;
                    static first = Owner.make();
                    static #secret = 7;
                    static make() {
                        const read = () => this.#secret;
                        return read();
                    }
                }
            } catch (error: any) {
                console.log(error.name);
            }
            """, "TypeError\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void StaticArrowsRetainPrivateReceiverBrands(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        new TypeChecker(TypeCheckerOptions.Strict).Check(TestHarness.ParseOrThrow(source));
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            var result = SourceExecutionService.Interpret(source);
            Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
            Assert.Empty(result.Errors);
            Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
        }
    }
}
