using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedArrayInheritedInvalidIndexTests
{
    [Theory, ModeData]
    public void InvalidIndicesStopBeforeReceiverAndValueConversion(ExecutionMode mode)
    {
        const string source = """
            const array = new Int32Array(2);
            const value = {valueOf(){throw 'converted';}};
            const receiver = new Proxy({}, {
                getOwnPropertyDescriptor(){throw 'descriptor';},
                defineProperty(){throw 'define';}
            });
            for (const key of ['NaN', '-0', '-1', '1.5', '2', 'Infinity', '-Infinity']) {
                const object = Object.create(array);
                object[key] = value;
                console.log(Object.getOwnPropertyDescriptor(object, key) === undefined);
                console.log(Reflect.set(array, key, value, receiver));
                console.log(Reflect.set(array, key, value, 1));
            }
            const strict = Object.preventExtensions(Object.create(array));
            (function(){'use strict'; strict.NaN = value;})();
            console.log(Object.getOwnPropertyDescriptor(strict, 'NaN') === undefined);
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat("true\n", 22)), TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void OrdinaryNamesValidIndicesAndNearerPropertiesRetainOrdinarySet(ExecutionMode mode)
    {
        const string source = """
            const array = new Int32Array([3, 4]);
            const object = Object.create(array);
            const value = {valueOf(){throw 'converted';}};
            for (const key of ['0', '01', '1.0', 'name']) {
                object[key] = value;
                console.log(Object.getOwnPropertyDescriptor(object, key).value === value);
            }
            console.log(array[0]);
            Object.defineProperty(object, 'NaN', {value: 1, writable: true});
            object.NaN = 7;
            console.log(object.NaN);
            const nearer = Object.create(array);
            Object.defineProperty(nearer, 'NaN', {set(v){console.log(this === child, v);}});
            const child = Object.create(nearer);
            child.NaN = 9;
            const throughProxy = Object.create(new Proxy(array, {}));
            throughProxy.NaN = value;
            console.log(Object.getOwnPropertyDescriptor(throughProxy, 'NaN') === undefined);
            """;
        Assert.Equal("true\ntrue\ntrue\ntrue\n3\n7\ntrue 9\ntrue\n", TestHarness.RunUnchecked(source, mode));
    }
}
