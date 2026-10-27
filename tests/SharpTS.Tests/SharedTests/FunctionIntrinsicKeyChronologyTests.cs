using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class FunctionIntrinsicKeyChronologyTests
{
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
