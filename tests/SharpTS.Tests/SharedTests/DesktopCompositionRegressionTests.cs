using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class DesktopCompositionRegressionTests
{
    [Theory, ModeData]
    public void NestedForOfEnumeratorsSurviveAsynchronousValidation(ExecutionMode mode)
    {
        Assert.Equal("1\n2\n3\n4\n1\n2\n3\n4\n", TestHarness.Run("""
            const pause = (): Promise<void> => new Promise<void>(resolve => setTimeout(() => resolve(), 1));
            async function validate(): Promise<void> {
                await pause();
                for (const row of [[1, 2], [3, 4]])
                    for (const value of row) { await pause(); console.log(value); }
            }
            const arrow = async (): Promise<void> => {
                for (const row of [[1, 2], [3, 4]])
                    for (const value of row) { await pause(); console.log(value); }
            };
            async function run(): Promise<void> { await validate(); await arrow(); }
            run();
            """, mode));
    }

    [Theory, ModeData]
    public void AsyncLoopBackedgesPreserveLocalsAndDetachedCallees(ExecutionMode mode)
    {
        Assert.Equal("ready\nready\nready\n", TestHarness.Run("""
            async function pause(): Promise<string> { return await new Promise<string>(resolve => setTimeout(() => resolve("ready"), 1)); }
            async function run(): Promise<void> {
                const limit = 2;
                let count = 0;
                while (count < limit) { console.log(await pause()); count++; }
                const read = (value: string): string => value;
                console.log(read(await pause()));
            }
            run();
            """, mode));
    }

    [Theory, ModeData]
    public void LabeledForOfContinuesAfterSuspension(ExecutionMode mode)
    {
        Assert.Equal("1\n3\n4\n", TestHarness.Run("""
            async function run(): Promise<void> {
                outer: for (const row of [[1, 2], [3, 4]]) {
                    for (const value of row) {
                        await new Promise<void>(resolve => setTimeout(() => resolve(), 1));
                        if (value === 2) continue outer;
                        console.log(value);
                    }
                }
            }
            run();
            """, mode));
    }

    [Theory, ModeData]
    public void AsyncArrowCatchPreservesTheErrorValue(ExecutionMode mode)
    {
        Assert.Equal("failure\n", TestHarness.Run("""
            const run = async (): Promise<void> => {
                try { await Promise.reject("failure"); }
                catch (error) { console.log(error); }
            };
            run();
            """, mode));
    }

    [Theory, ModeData]
    public void AsyncArrowPreservesSnapshotsCreatedBetweenAwaits(ExecutionMode mode)
    {
        Assert.Equal("saved\n", TestHarness.Run("""
            const pause = (): Promise<void> => new Promise<void>(resolve => setTimeout(() => resolve(), 1));
            const save = async (): Promise<void> => {
                await pause();
                const snapshot = { name: "saved" };
                await pause();
                console.log(snapshot.name);
            };
            save();
            """, mode));
    }

    [Theory, ModeData]
    public void InstantiatedGenericInterfaceSupportsMutablePropertyWrites(ExecutionMode mode)
    {
        const string source = """
            interface Ref<T> { current: T; }
            function update(ref: Ref<string>): void { ref.current = "after"; }
            const ref: Ref<string> = { current: "before" };
            update(ref);
            console.log(ref.current);
            """;
        Assert.Equal("after\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void InstantiatedGenericInterfaceStillRejectsReadonlyAndWrongTypeWrites()
    {
        Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted("""
            interface Ref<T> { readonly current: T; }
            const ref: Ref<string> = { current: "before" };
            ref.current = "after";
            """));
        Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted("""
            interface Ref<T> { current: T; }
            const ref: Ref<string> = { current: "before" };
            ref.current = 42;
            """));
    }

    [Theory, ModeData]
    public void ArrayCallbacksReceiveRequiredIndexAndArrayArguments(ExecutionMode mode)
    {
        Assert.Equal("20,30\n1\ntrue\n", TestHarness.Run("""
            const values = [10, 20, 30];
            console.log(values.filter((value: number, index: number) => index > 0).join(","));
            console.log(values.findIndex((value: number, index: number) => index === 1));
            console.log(values.every((value: number, index: number, array: number[]) => value === array[index]));
            """, mode));
    }

    [Fact]
    public void VoidPromiseUnionHasVerifiableStorageAndAccessors()
    {
        Assert.Empty(TestHarness.CompileAndVerifyOnly("""
            interface Command { execute: () => void | Promise<void>; }
            const command: Command = { execute: () => {} };
            const result: void | Promise<void> = command.execute();
            console.log(result === undefined);
            """));
    }
}
