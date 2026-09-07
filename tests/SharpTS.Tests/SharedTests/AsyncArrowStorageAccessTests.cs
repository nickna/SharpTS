using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class AsyncArrowStorageAccessTests
{
    // Exercise the same programs in both engines, strict IL verification, and an isolated
    // process without SharpTS.dll. Assignment expressions also check the retained stack value.
    public static IEnumerable<object[]> Programs()
    {
        yield return ["outer display class", """
            async function main() {
                let value: any = 0;
                let calls = 0;
                const write = async () => {
                    console.log(value = 2);
                    console.log(value += 3);
                    console.log(value++);
                    await new Promise<number>(resolve => setTimeout(() => resolve(0), 1));
                    console.log(++value);
                    console.log(value ||= ++calls);
                    console.log(value &&= 9);
                    value = null;
                    console.log(value ??= 11);
                    console.log(value ??= ++calls);
                };
                await write();
                console.log(value, calls);
            }
            main();
            """, "2\n5\n5\n7\n7\n9\n11\n11\n11 0\n"];

        yield return ["own display class and block shadow", """
            const run = async () => {
                let value = 100;
                {
                    let value: any = 1;
                    const bump = () => { value += 1; return value; };
                    console.log(value = 3);
                    console.log(value += 2);
                    console.log(value++);
                    console.log(bump());
                    await new Promise<number>(resolve => setTimeout(() => resolve(0), 1));
                    console.log(++value);
                    console.log(value &&= 9);
                    console.log(value ||= 20);
                    value = null;
                    console.log(value ??= 10);
                    console.log(bump(), value);
                }
                console.log(value);
            };
            run();
            """, "3\n5\n5\n7\n8\n9\n9\n10\n11 11\n100\n"];

        yield return ["transitive outer capture and sync population", """
            async function main(seed: number) {
                const offset = 3;
                const outer = async () => {
                    const inner = async () => {
                        await Promise.resolve(0);
                        const read = () => seed + offset;
                        return read() + seed;
                    };
                    return await inner();
                };
                console.log(await outer());
            }
            main(10);
            """, "23\n"];

        yield return ["standalone relay and hoisted parameter", """
            const run = async (seed: number) => {
                console.log(seed = 4);
                console.log(seed += 2);
                await Promise.resolve(0);
                console.log(seed++);
                console.log(++seed);
                const offset = 3;
                const outer = async () => {
                    const inner = async () => {
                        const read = () => seed + offset;
                        return read();
                    };
                    return await inner();
                };
                console.log(await outer());
            };
            run(1);
            """, "4\n6\n6\n8\n11\n"];

        yield return ["live module storage", """
            let value: any = 1;
            const run = async () => {
                console.log(value);
                console.log(value = 4);
                console.log(value += 2);
                console.log(value++);
                await Promise.resolve(0);
                console.log(++value);
                console.log(value &&= 9);
                value = null;
                console.log(value ??= 10);
                console.log(value ||= 20);
            };
            value = 3;
            async function main() {
                await run();
                console.log(value);
            }
            main();
            """, "3\n4\n6\n6\n8\n9\n10\n10\n10\n"];

        yield return ["per-iteration cells", """
            const run = async () => {
                const readers: any[] = [];
                for (let i = 0; i < 2; i++) {
                    i = i + 10;
                    console.log(i += 1);
                    console.log(i++);
                    console.log(--i);
                    i -= 11;
                    readers.push(() => i);
                }
                await Promise.resolve(0);
                console.log(readers.map((read: any) => read()).join(","));
            };
            run();
            """, "11\n11\n11\n12\n12\n12\n0,1\n"];
    }

    public static IEnumerable<object[]> ProgramsInBothModes()
        => Programs().SelectMany(program => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { program[0], program[1], program[2], mode }));

    [Theory]
    [MemberData(nameof(ProgramsInBothModes))]
    public void StorageAccess_PreservesValues(string scenario, string source, string expected, ExecutionMode mode)
        => AssertOutput(scenario, expected, TestHarness.Run(source, mode));

    [Theory]
    [MemberData(nameof(Programs))]
    public void StorageAccess_PassesILVerification(string scenario, string source, string expected)
    {
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.True(errors.Count == 0, $"{scenario}: {string.Join(Environment.NewLine, errors)}");
        AssertOutput(scenario, expected, output);
    }

    [Theory]
    [MemberData(nameof(Programs))]
    public void StorageAccess_RunsWithoutSharpTsDll(string scenario, string source, string expected)
        => AssertOutput(scenario, expected, TestHarness.RunCompiledStandalone(source));

    private static void AssertOutput(string scenario, string expected, string actual)
        => Assert.True(expected == actual, $"{scenario}\nExpected:\n{expected}\nActual:\n{actual}");
}
