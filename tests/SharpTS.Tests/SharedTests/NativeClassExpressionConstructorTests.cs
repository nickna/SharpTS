using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NativeClassExpressionConstructorTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["implicit-native-array-expression-issue", """
            const Values = class extends Array<number> {};
            const value: any = new Values(1, 2);
            console.log(value.length);
            """, "2\n"];
        yield return ["implicit-native-promise-expression-issue", """
            const Pending = class extends Promise<number> {};
            const value: any = new Pending(resolve => resolve(7));
            value.then((result: number) => console.log(result));
            """, "7\n"];
        yield return ["implicit-native-array-expression-declare-fields-erase", """
            const Values = class extends Array<number> {
                declare tag: string;
                actual = 5;
            };
            const value: any = new Values(1, 2);
            console.log(Array.isArray(value), value.length, value[0], value[1], value.actual);
            console.log(Object.hasOwn(value, "tag"), "tag" in value, value.tag === undefined);
            """, "true 2 1 2 5\nfalse false true\n"];
        yield return ["implicit-native-promise-expression-declare-fields-erase", """
            const Pending = class extends Promise<number> {
                declare tag: string;
                actual = 5;
            };
            const value: any = new Pending((resolve: any) => resolve(7));
            console.log(Object.hasOwn(value, "tag"), value.actual, typeof value.then);
            value.then((result: number) => console.log(result, value.actual));
            """, "false 5 function\n7 5\n"];
        yield return ["implicit-native-array-declaration-control", """
            class Values extends Array<number> {}
            const value: any = new Values(1, 2);
            console.log(value.length);
            """, "2\n"];
        yield return ["implicit-native-promise-declaration-control", """
            class Pending extends Promise<number> {}
            const value: any = new Pending(resolve => resolve(7));
            value.then((result: number) => console.log(result));
            """, "7\n"];
        // #2808 owns generic iterator substitution; preserved evidence is under
        // artifacts/p2-goal/2708-generic-iterator-owner-audit; the full generic
        // positive and negative sources remain in 2708-node-oracle for restoration.
        yield return ["concrete-native-array-expression-retains-iterator-element-types", """
            const Numbers = class extends Array<number> {};
            const Strings = class extends Array<string> {};
            const numbers = new Numbers(1, 2), strings = new Strings("a", "b");
            let sum = 0, text = "";
            for (const item of numbers) { const value: number = item; sum += value; }
            for (const item of strings) { const value: string = item; text += value; }
            console.log(sum, text, numbers.length, strings.length);
            """, "3 ab 2 2\n"];
    }

    public static IEnumerable<object[]> SavedCases() => Cases();

    public static IEnumerable<object[]> ModeCases()
    {
        foreach (var row in Cases())
            yield return [row[0], row[1], row[2], ExecutionMode.Interpreted];
        foreach (var row in SavedCases())
            yield return [row[0], row[1], row[2], ExecutionMode.Compiled];
    }

    [Theory, MemberData(nameof(ModeCases))]
    public void NativeFallbackImplicitConstructorsForwardValidArguments(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        // The bare checker uses native fallback globals without declaration libraries.
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
