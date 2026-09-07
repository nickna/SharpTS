using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class DynamicIteratorResultTests
{
    [Theory, ModeData]
    public void NumericCaptureFallbacksPreserveUndefinedShadowingAndMultipleClosures(ExecutionMode mode)
    {
        const string source = """
            function missing(n?: number): any {
                const object: any = { read() { return n; } };
                return object;
            }
            console.log(missing().read());
            function shared(): any {
                let current: number = 1;
                const object: any = {
                    read() { return current; },
                    write(n: number) { current = n; }
                };
                return object;
            }
            const object = shared();
            object.write(7);
            console.log(object.read());
            function shadowed(): any {
                let current: number = 1;
                const object: any = { read() { return current++; } };
                function show(current: number) { console.log(current); }
                show(20);
                return object;
            }
            const shadow = shadowed();
            console.log(shadow.read(), shadow.read());
            """;
        Assert.Equal("undefined\n7\n20\n1 2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void LexicalArrowAndBoundNextKeepTheirReceivers(ExecutionMode mode)
    {
        const string source = """
            class Factory {
                base: number = 10;
                create() {
                    let index = 0;
                    return {
                        [Symbol.iterator]() { return this; },
                        next: () => ({ value: this.base + index++, done: index > 2 })
                    };
                }
            }
            let sum = 0;
            for (const value of new Factory().create()) sum = sum + value;
            console.log(sum);
            let index = 0;
            const next: any = function() { return { value: this.base + index++, done: index > 2 }; };
            const iterator: any = { [Symbol.iterator]() { return this; }, next: next.bind({ base: 20 }) };
            sum = 0;
            for (const value of iterator) sum = sum + value;
            console.log(sum);
            """;
        Assert.Equal("21\n41\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ProxyResultReadsDoneBeforeValue(ExecutionMode mode)
    {
        const string source = """
            let index = 0;
            let log = "";
            const iterator: any = {
                [Symbol.iterator]() { return this; },
                next() {
                    index++;
                    return new Proxy({ value: 3, done: index > 1 }, {
                        get(target: any, key: any) { log += key + ","; return target[key]; }
                    });
                }
            };
            let sum = 0;
            for (const value of iterator) sum = sum + value;
            console.log(sum, log);
            """;
        Assert.Equal("3 done,value,done,\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ResultsRemainDistinctMutableOrdinaryObjects(ExecutionMode mode)
    {
        const string source = """
            function result(n: number) { return { value: n, done: false }; }
            const first: any = result(1);
            const second: any = result(2);
            console.log(first === second, Object.keys(first).join(","));
            first.value = 8;
            first.extra = 9;
            delete first.done;
            console.log(first.value, first.extra, second.value, second.done);
            console.log(Object.getOwnPropertyDescriptor(second, "value").writable);
            """;
        Assert.Equal("false value,done\n8 9 2 false\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void RedefinedResultPropertiesAreObserved(ExecutionMode mode)
    {
        const string source = """
            let index = 0;
            let log = "";
            function result() { return { value: 1, done: false }; }
            const iterator: any = {
                [Symbol.iterator]() { return this; },
                next() {
                    const r: any = result();
                    index++;
                    Object.defineProperty(r, "done", { get() { log += "d"; return index > 1; } });
                    Object.defineProperty(r, "value", { get() { log += "v"; return 7; } });
                    return r;
                }
            };
            let sum = 0;
            for (const value of iterator) sum += value;
            console.log(sum, log);
            """;
        Assert.Equal("7 dvd\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ChangedResultShapeRetainsAdditionSemantics(ExecutionMode mode)
    {
        const string source = """
            let index = 0;
            const iterator: any = {
                [Symbol.iterator]() { return this; },
                next() {
                    index++;
                    if (index === 1) return { value: 1, done: false };
                    if (index === 2) return { done: false, value: "x" };
                    return { value: 0, done: true };
                }
            };
            let sum: any = 0;
            for (const value of iterator) sum = sum + value;
            console.log(sum);
            """;
        Assert.Equal("1x\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ZeroArgumentNextPreservesDefaultsRestAndArguments(ExecutionMode mode)
    {
        const string source = """
            let count = 0;
            const iterator: any = {
                [Symbol.iterator]() { return this; },
                next(first = 4, ...rest: any[]) {
                    console.log(first, rest.length, arguments.length, this === iterator);
                    count++;
                    return { value: 2, done: count > 1 };
                }
            };
            for (const value of iterator) console.log(value);
            """;
        Assert.Equal("4 0 0 true\n2\n4 0 0 true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void EscapedNumericClosureKeepsLiveUpdates(ExecutionMode mode)
    {
        const string source = """
            function create(n: number): any {
                let current: number = 0;
                const object: any = { next() { return current++ + n; } };
                current = 3;
                return object;
            }
            const object: any = create(10);
            console.log(object.next(), object.next());
            console.log(Number.isNaN(create(NaN).next()), create(Infinity).next());
            """;
        Assert.Equal("13 14\ntrue Infinity\n", TestHarness.Run(source, mode));
    }
}
