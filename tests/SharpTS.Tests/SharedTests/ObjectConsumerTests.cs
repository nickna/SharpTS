using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ObjectConsumerTests
{
    [Theory, ModeData]
    public void MutatedSpreadSources_PreserveDeletionAndIncrement(ExecutionMode mode)
    {
        const string source = """
            function retain(value: any): any { return value; }
            function work(n: number): void {
                const removed = { a: n, b: n + 1 };
                delete removed.a;
                const deletedResult = { ...removed };
                console.log(Object.keys(retain(deletedResult)).join(","));
                const incremented = { a: n };
                console.log(incremented.a++);
                const postResult = { ...incremented };
                console.log(retain(postResult).a);
                console.log(++incremented.a);
                const preResult = { ...incremented };
                console.log(retain(preResult).a);
            }
            work(1);
            """;
        Assert.Equal("b\n1\n2\n3\n3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NumericConsumer_WritesOriginalSlotInOrder(ExecutionMode mode)
    {
        const string source = """
            function consume(value: any): number {
                value.d = value.d + 1;
                value.a = value.a * (value.d - value.b);
                return (-value.a + value.d) / 2 + value.b % 2;
            }
            function work(a: number, b: number, d: number): number {
                const original = { a: a, b: b, d: d };
                const result = { ...original };
                console.log(consume(result));
                console.log(result.a);
                console.log(result.d);
                return original.a + original.d;
            }
            console.log(work(2, 3, 5));
            """;
        Assert.Equal("1\n6\n6\n7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void RetainedResult_MaterializesWithoutAliasingSource(ExecutionMode mode)
    {
        const string source = """
            let saved: any;
            function retain(value: any): number { saved = value; return value.a; }
            function work(a: number, label: string, enabled: boolean): void {
                const original = { a: a, label: label, enabled: enabled };
                const result = { ...original, middle: (original.a = a + 2), ...original };
                console.log(retain(result));
                saved.a = 10;
                console.log(result.a);
                console.log(original.a);
                console.log(result.label);
                console.log(result.enabled);
                console.log(Object.keys(result).join(","));
                original.a = 20;
                console.log(saved.a);
            }
            work(1, "first", true);
            """;
        Assert.Equal("3\n10\n3\nfirst\ntrue\na,label,enabled,middle\n10\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ShadowedConsumer_UsesActualParameter(ExecutionMode mode)
    {
        const string source = """
            function consume(value: any): number { value.a = value.a + 1; return value.a; }
            function work(consume: (value: any) => number, n: number): number {
                const original = { a: n };
                const result = { ...original };
                return consume(result) + result.a;
            }
            console.log(work((value: any): number => { value.a = 20; return 100; }, 1));
            """;
        Assert.Equal("120\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ReassignedLocalConsumer_UsesReplacement(ExecutionMode mode)
    {
        const string source = """
            function consume(value: any): number { value.a = value.a + 1; return value.a; }
            function replacement(value: any): number { value.a = 9; return 100; }
            function work(n: number): number {
                let current = consume;
                current = replacement;
                const original = { a: n };
                const result = { ...original };
                return current(result) + result.a;
            }
            console.log(work(1));
            """;
        Assert.Equal("109\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ModuleLocalConsumers_WithSameNameRemainIndependent(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["remote.ts"] = """
                function consume(value: any): number { value.a = value.a + 100; return value.a; }
                export function remoteWork(n: number): number {
                    const remoteSource = { a: n };
                    const remoteResult = { ...remoteSource };
                    return consume(remoteResult);
                }
                """,
            ["main.ts"] = """
                import { remoteWork } from './remote';
                function consume(value: any): number { value.a = value.a + 1; return value.a; }
                function work(n: number): number {
                    const localSource = { a: n };
                    const localResult = { ...localSource };
                    return consume(localResult);
                }
                console.log(work(1), remoteWork(1));
                """
        };
        Assert.Equal("2 101\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void ReassignedSource_UsesNewValueForLaterSpread(ExecutionMode mode)
    {
        const string source = """
            function retain(value: any): any { return value; }
            function work(n: number): void {
                let original = { a: n };
                const before = { ...original };
                original = { a: n + 6 };
                const after = { ...original };
                console.log(retain(before).a);
                console.log(retain(after).a);
            }
            work(1);
            """;
        Assert.Equal("1\n7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void MissingOrNonNumericFields_RetainDynamicSemantics(ExecutionMode mode)
    {
        const string source = """
            function missing(value: any): number { value.d = value.d + 1; return value.d; }
            function concatenate(value: any): any { value.a = value.a + 1; return value.a; }
            function work(n: number, s: string): void {
                const incomplete = { a: n };
                const missingResult = { ...incomplete };
                console.log(Number.isNaN(missing(missingResult)));
                const text = { a: s };
                const textResult = { ...text };
                console.log(concatenate(textResult));
                console.log(typeof textResult.a);
            }
            work(1, "2");
            """;
        Assert.Equal("true\n21\nstring\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SharedParameterName_DoesNotConfuseLocalStorage(ExecutionMode mode)
    {
        const string source = """
            function consume(value: any): number { value.a = value.a + 1; return value.a; }
            function work(n: number): number {
                const original = { a: n };
                const result = { ...original };
                return consume(result);
            }
            function other(result: any): number { return consume(result); }
            console.log(work(1));
            console.log(other({ a: 9 }));
            """;
        Assert.Equal("2\n10\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ConsumerWithCallOrClosure_RetainsReferenceSemantics(ExecutionMode mode)
    {
        const string source = """
            let later: () => number = () => 0;
            function consume(value: any): number {
                later = () => value.a;
                value.a = value.a + 1;
                return value.a;
            }
            function work(n: number): number {
                const original = { a: n };
                const result = { ...original };
                consume(result);
                result.a = 10;
                return later() + original.a;
            }
            console.log(work(1));
            """;
        Assert.Equal("11\n", TestHarness.Run(source, mode));
    }
}
