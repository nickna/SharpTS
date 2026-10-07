using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class RepeatedClassAsyncGeneratorCaptureTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["instance-arrow-before-await", """
            function make(seed: number) {
                return class {
                    async *values(): AsyncGenerator<any> {
                        const value = 100;
                        {
                            const value = seed;
                            const read = () => value;
                            await new Promise<any>(resolve => setTimeout(() => resolve(0), 1));
                            yield read;
                        }
                        yield value;
                        yield () => seed;
                    }
                };
            }
            async function run() {
                const First = make(7), Second = make(9);
                const first = new First().values(), second = new Second().values();
                const one = await first.next(), two = await second.next();
                console.log(one.value(), two.value());
                console.log((await first.next()).value, (await second.next()).value);
                const outerOne = await first.next(), outerTwo = await second.next();
                console.log(outerOne.value(), outerTwo.value(), one.value(), two.value());
            }
            run();
            """, "7 9\n100 100\n7 9 7 9\n"];
        yield return ["static-arrow-after-await", """
            function make(seed: number) {
                return class {
                    static async *values(): AsyncGenerator<any> {
                        const value = 100;
                        {
                            const value = seed;
                            await new Promise<any>(resolve => setTimeout(() => resolve(0), 1));
                            yield () => value;
                        }
                        yield value;
                        yield () => seed;
                        yield this;
                    }
                };
            }
            async function run() {
                const First = make(7), Second = make(9);
                const first = First.values(), second = Second.values();
                const one = await first.next(), two = await second.next();
                console.log(one.value(), two.value());
                console.log((await first.next()).value, (await second.next()).value);
                const outerOne = await first.next(), outerTwo = await second.next();
                console.log(outerOne.value(), outerTwo.value(), one.value(), two.value());
                console.log((await first.next()).value === First, (await second.next()).value === Second);
            }
            run();
            """, "7 9\n100 100\n7 9 7 9\ntrue true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void DefinitionAndShadowCapturesSurviveSuspension(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
        Assert.Equal(expected, TestHarness.RunCompiled(source));
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
        Assert.Equal(expected, TestHarness.RunCompiledStandalone(source));
    }
}
