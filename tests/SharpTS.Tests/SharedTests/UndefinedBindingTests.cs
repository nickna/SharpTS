using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class UndefinedBindingTests
{
    [Theory, ModeData]
    public void ParametersAndLocalsShadowUndefined(ExecutionMode mode)
    {
        const string source = """
            function f(undefined: number): number { undefined = undefined + 1; return undefined; }
            console.log(f(7));
            const arrow = (undefined: string): string => undefined;
            console.log(arrow('local'));
            function local(): number {
                let undefined = 2;
                const capture = () => undefined;
                undefined++;
                return capture();
            }
            console.log(local());
            function length(value: string | undefined): number {
                if (value !== undefined) return value.length;
                return 0;
            }
            console.log(length('abc'), length(undefined));
            console.log(undefined === void 0, typeof undefined);
            """;
        Assert.Equal("8\nlocal\n3\n3 0\ntrue undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SuspendedParametersAndWithBindingsShadowUndefined(ExecutionMode mode)
    {
        const string source = """
            function* generator(undefined) { yield undefined; }
            console.log(generator(7).next().value);
            async function run(undefined) { await 0; return undefined; }
            async function main() { console.log(await run(8)); }
            const pending = main();
            with ({ undefined: 9 }) console.log(undefined);
            console.log(undefined === void 0);
            """;
        Assert.Equal("7\n9\ntrue\n8\n", TestHarness.RunUnchecked(source, mode));
    }
}
