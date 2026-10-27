using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class FunctionIntrinsicKeyChronologyTests
{
    [Theory, ModeData]
    public void ExplicitIntrinsicDescriptorsControlEnumerableAdmission(ExecutionMode mode)
    {
        const string source = """
            for (const key of ['name', 'length']) {
                const fn: any = () => {};
                Object.defineProperty(fn, key, { value: 7, enumerable: true, configurable: true });
                console.log(Object.prototype.propertyIsEnumerable.call(fn, key), JSON.stringify(Object.values(fn)),
                    JSON.stringify(Object.entries(fn)), JSON.stringify(Object.assign({}, fn)));
                Object.defineProperty(fn, key, { enumerable: false });
                console.log(Object.prototype.propertyIsEnumerable.call(fn, key), JSON.stringify(Object.values(fn)),
                    JSON.stringify(Object.entries(fn)), JSON.stringify(Object.assign({}, fn)));
            }
            """;
        Assert.Equal("true [7] [[\"name\",7]] {\"name\":7}\nfalse [] [] {}\ntrue [7] [[\"length\",7]] {\"length\":7}\nfalse [] [] {}\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void CompiledIntrinsicAccessorAdmissionDoesNotInvokeItsGetter()
    {
        const string source = """
            for (const key of ['name', 'length']) {
                const fn: any = () => {};
                let reads = 0;
                const getter = () => { reads++; return 9; };
                Object.defineProperty(fn, key, { get: getter, enumerable: true });
                console.log(Object.prototype.propertyIsEnumerable.call(fn, key), reads);
                console.log(JSON.stringify(Object.values(fn)), reads);
                console.log(JSON.stringify(Object.entries(fn)), reads);
                console.log(JSON.stringify(Object.assign({}, fn)), reads);
            }
            """;
        Assert.Equal("true 0\n[9] 1\n[[\"name\",9]] 2\n{\"name\":9} 3\ntrue 0\n[9] 1\n[[\"length\",9]] 2\n{\"length\":9} 3\n", TestHarness.Run(source, ExecutionMode.Compiled));
    }

    [Theory, ModeData]
    public void RedefinitionKeepsIntrinsicOrderWhileRecreationMovesTheKey(ExecutionMode mode)
    {
        const string source = """
            const fn: any = () => {};
            fn.a = 1;
            Object.defineProperty(fn, 'name', { enumerable: true });
            Object.defineProperty(fn, 'length', { enumerable: true });
            console.log(Object.entries(fn).map((entry: any) => entry[0]).join(','));
            console.log(Object.getOwnPropertyNames(fn).join(','));
            delete fn.name;
            fn.b = 2;
            Object.defineProperty(fn, 'name', { value: 'again', enumerable: true, configurable: true });
            console.log(Object.entries(fn).map((entry: any) => entry[0]).join(','));
            console.log(Object.getOwnPropertyNames(fn).join(','));
            console.log(Object.keys(fn).join(','));
            """;
        Assert.Equal("length,name,a\nlength,name,a\nlength,a,b,name\nlength,a,b,name\nlength,a,b,name\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void PrototypePositionDistinguishesIntrinsicAndAddedSlots(ExecutionMode mode)
    {
        const string source = """
            function declared() {}
            const arrow: any = () => {};
            const ordinary: any = declared;
            ordinary.a = 1;
            arrow.a = 1;
            Object.defineProperty(arrow, 'prototype', { value: {}, enumerable: true });
            console.log(Object.getOwnPropertyNames(ordinary).join(','));
            console.log(Object.getOwnPropertyNames(arrow).join(','));
            console.log(Object.entries(arrow).map((entry: any) => entry[0]).join(','));
            console.log(Object.keys(arrow).join(','));
            """;
        Assert.Equal("length,name,prototype,a\nlength,name,a,prototype\na,prototype\na,prototype\n", TestHarness.Run(source, mode));
    }
}
