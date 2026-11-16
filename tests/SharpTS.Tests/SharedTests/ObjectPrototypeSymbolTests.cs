using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ObjectPrototypeSymbolTests
{
    [Theory, ModeData]
    public void AssignedSymbolsAreInheritedAndRetainDescriptors(ExecutionMode mode)
    {
        const string source = """
            const key = Symbol('probe');
            const other = Symbol('probe');
            Object.prototype[key] = 11;
            console.log(Object.prototype[key], ({})[key], Boolean.prototype[key]);
            console.log(Object.prototype[other] === undefined);
            const descriptor = Object.getOwnPropertyDescriptor(Object.prototype, key);
            console.log(descriptor.value, descriptor.writable, descriptor.enumerable, descriptor.configurable);
            console.log(Object.getOwnPropertySymbols(Object.prototype)[0] === key);
            console.log(Reflect.ownKeys(Object.prototype).includes(key));
            console.log(Object.getOwnPropertyDescriptors(Object.prototype)[key].value);
            console.log(key in {}, Object.hasOwn(Object.prototype, key));
            console.log(delete Object.prototype[key], key in {});
            """;
        Assert.Equal("11 11 11\ntrue\n11 true true true\ntrue\ntrue\n11\ntrue true\ntrue false\n",
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void SymbolAccessorsAndIntegrityUseTheOriginalReceiver(ExecutionMode mode)
    {
        const string source = """
            const key = Symbol('accessor');
            Object.defineProperty(Object.prototype, key, {
                get() { return this.value; },
                set(value) { this.value = value; },
                configurable: true
            });
            const child = {};
            child[key] = 7;
            console.log(child[key], Object.hasOwn(child, key));
            Object.prototype[key] = 9;
            console.log(Object.prototype.value);
            console.log(Reflect.deleteProperty(Object.prototype, key));
            console.log(Reflect.defineProperty(Object.prototype, key, { value: 3, writable: true, configurable: true }));
            Object.freeze(Object.prototype);
            console.log(Reflect.set(Object.prototype, key, 4));
            console.log(Reflect.defineProperty(Object.prototype, Symbol(), { value: 5 }));
            console.log(Reflect.deleteProperty(Object.prototype, key));
            try { (function() { 'use strict'; Object.prototype[key] = 4; })(); }
            catch (error) { console.log(error instanceof TypeError); }
            console.log(Object.prototype[key]);
            """;
        Assert.Equal("7 false\n9\ntrue\ntrue\nfalse\nfalse\nfalse\ntrue\n3\n",
            TestHarness.RunUnchecked(source, mode));
    }
}
