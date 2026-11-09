using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class JSONReviverPropertyTests
{
    [Theory, ModeData]
    public void ReviverPreservesTheDistinctionBetweenUndefinedAndNullInstanceFields(ExecutionMode mode)
    {
        const string source = """
            class Replacement { missing = undefined; present = null; }
            const result = JSON.parse('{"first":0,"last":{}}', function (key, value) {
                if (key === 'first') this.last = new Replacement();
                if (key === 'missing' || key === 'present')
                    console.log(key, value === undefined, value === null);
                return value;
            });
            console.log(Object.hasOwn(result.last, 'missing'), Object.hasOwn(result.last, 'present'));
            """;
        Assert.Equal("missing true false\npresent false true\nfalse true\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void ReviverReadsAndReplacesAnInjectedArrayIndexGetter(ExecutionMode mode)
    {
        const string source = """
            let reads = 0;
            let writes = 0;
            const result = JSON.parse('[1,2]', function (key, value) {
                if (key === '0') {
                    const holder = this;
                    Object.defineProperty(this, '1', {
                        get() { reads++; return this === holder ? 7 : 99; },
                        set() { writes++; },
                        configurable: true, enumerable: true
                    });
                }
                if (key === '1') {
                    console.log(value);
                    return 8;
                }
                return value;
            });
            const descriptor = Object.getOwnPropertyDescriptor(result, '1');
            console.log(reads, writes, result[1], descriptor.value);
            """;
        Assert.Equal("7\n1 0 8 8\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void ReviverReadsAndReplacesAnInjectedGetter(ExecutionMode mode)
    {
        const string source = """
            let reads = 0;
            let writes = 0;
            let holder;
            const values = [];
            const result = JSON.parse('{"first":0,"last":{"a":1}}', function (key, value) {
                if (key === 'first') {
                    holder = this.last;
                    Object.defineProperty(holder, 'injected', {
                        get() { reads++; return this === holder ? 7 : 99; },
                        set() { writes++; },
                        configurable: true, enumerable: true
                    });
                }
                if (key === 'injected') values.push(value);
                return key === 'injected' ? 8 : value;
            });
            const descriptor = Object.getOwnPropertyDescriptor(result.last, 'injected');
            console.log(reads, writes, JSON.stringify(values), result.last.injected);
            console.log(descriptor.value, descriptor.writable, descriptor.enumerable, descriptor.configurable);
            """;
        Assert.Equal("1 0 [7] 8\n8 true true true\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void ReviverReadsButDoesNotReplaceANonConfigurableGetter(ExecutionMode mode)
    {
        const string source = """
            let reads = 0;
            const result = JSON.parse('{"a":1,"b":2}', function (key, value) {
                if (key === 'a') Object.defineProperty(this, 'b', {
                    get() { reads++; return 7; },
                    configurable: false, enumerable: true
                });
                if (key === 'b') {
                    console.log(value);
                    return 8;
                }
                return value;
            });
            console.log(result.b, reads);
            """;
        Assert.Equal("7\n7 2\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void ReviverReadsOwnValuesThatShadowObjectPrototypeMethods(ExecutionMode mode)
    {
        const string source = """
            const result = JSON.parse('{"hasOwnProperty":7,"isPrototypeOf":8}', function (key, value) {
                if (key) console.log(key, value);
                return value;
            });
            console.log(JSON.stringify(result));
            """;
        Assert.Equal("hasOwnProperty 7\nisPrototypeOf 8\n{\"hasOwnProperty\":7,\"isPrototypeOf\":8}\n",
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void ReviverDoesNotRestoreADeletedKeyOnANonExtensibleHolder(ExecutionMode mode)
    {
        const string source = """
            const result = JSON.parse('{"a":1,"b":2,"c":3}', function (key, value) {
                if (key === 'a') {
                    delete this.b;
                    this.c = undefined;
                    Object.preventExtensions(this);
                }
                if (key === 'b' || key === 'c') {
                    console.log(key, value === undefined);
                    return 8;
                }
                return value;
            });
            console.log(Object.hasOwn(result, 'b'), result.c);
            """;
        Assert.Equal("b true\nc true\nfalse 8\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void ReviverReadsAnInheritedGetterWithTheHolderAsReceiver(ExecutionMode mode)
    {
        const string source = """
            const prototype = { get inherited() { return this.a + 6; } };
            const result = JSON.parse('{"a":1,"inherited":2}', function (key, value) {
                if (key === 'a') {
                    delete this.inherited;
                    Object.setPrototypeOf(this, prototype);
                }
                if (key === 'inherited') {
                    console.log(value);
                    return 8;
                }
                return value;
            });
            console.log(Object.hasOwn(result, 'inherited'), result.inherited);
            """;
        Assert.Equal("7\ntrue 8\n", TestHarness.RunUnchecked(source, mode));
    }
}
