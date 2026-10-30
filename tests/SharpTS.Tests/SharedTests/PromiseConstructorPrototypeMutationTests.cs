using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PromiseConstructorPrototypeMutationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["live-native-static-presence-and-species", """
            class P extends Promise<any> { static own() { return 5; } }
            const owner: any = P, initial = Object.getPrototypeOf(owner), species = Symbol.species;
            const source: any = new P(resolve => resolve(7));
            const own = owner.own, custom = { marker: 9 };
            function observe(label: string, expected: any) {
                console.log(label, Object.getPrototypeOf(owner) === expected, typeof owner.resolve,
                    owner[species] === owner, "resolve" in owner, species in owner,
                    Reflect.has(owner, "resolve"), Reflect.has(owner, species),
                    owner.own === own, owner.own());
            }
            async function run() {
                observe("initial", initial);
                Object.setPrototypeOf(owner, Promise);
                observe("native", Promise);
                const nativeConstructed: any = new P(resolve => resolve(11)), nativeResult = source.then();
                console.log("native-results", nativeConstructed instanceof Promise, nativeConstructed instanceof P,
                    nativeResult instanceof Promise, nativeResult instanceof P);
                Object.setPrototypeOf(owner, custom);
                observe("custom", custom);
                const customResult = source.then();
                console.log("custom-result", customResult instanceof Promise, customResult instanceof P);
                Object.setPrototypeOf(owner, null);
                observe("null", null);
                const nullResult = source.then();
                console.log("null-result", nullResult instanceof Promise, nullResult instanceof P);
                Object.setPrototypeOf(owner, initial);
                observe("restored", initial);
                const restoredResult = source.then();
                console.log("restored-result", restoredResult instanceof Promise, restoredResult instanceof P);
                console.log("values", (await Promise.all([
                    nativeConstructed, nativeResult, customResult, nullResult, restoredResult])).join(","));
            }
            run().catch((error: any) => console.log("unexpected", error.name, error.message));
            """, "initial true function true true true true true true 5\n"
                + "native true function true true true true true true 5\n"
                + "native-results true true true true\n"
                + "custom true undefined false false false false false true 5\n"
                + "custom-result true false\n"
                + "null true undefined false false false false false true 5\n"
                + "null-result true false\n"
                + "restored true function true true true true true true 5\n"
                + "restored-result true true\nvalues 11,7,7,7,7\n"];
    }

    public static IEnumerable<object[]> ModeCases()
    {
        foreach (var row in Cases())
        {
            yield return [row[0], row[1], row[2], ExecutionMode.Interpreted];

            yield return [row[0], row[1], row[2], ExecutionMode.Compiled];
        }
    }

    [Theory, MemberData(nameof(ModeCases))]
    public void PromiseStaticsAndSpeciesFollowTheLiveConstructorPrototype(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            Assert.Equal(expected, TestHarness.Run(source, mode));
        }
    }
}
