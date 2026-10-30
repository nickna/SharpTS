using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ObjectConstructorIntegrityTests
{
    // Compiled frozen string properties reject compatible definitions (#2575).
    [Fact]
    public void FrozenObjectsAcceptCompatibleDefinitions()
    {
        const string source = """
            for (const target of [Object, {}] as any[]) {
                let reads = 0;
                const getter = function() { reads++; return 1; };
                Object.defineProperty(target, 'value', { value: -0, configurable: true });
                Object.defineProperty(target, 'accessor', { get: getter, configurable: true });
                Object.freeze(target);
                console.log(Object.defineProperty(target, 'value', {}) === target,
                    Object.defineProperty(target, 'value', { value: -0 }) === target,
                    Object.defineProperty(target, 'accessor', { get: getter }) === target, reads);
                for (const descriptor of [{ value: 0 }, { writable: true }, { configurable: true }]) {
                    try { Object.defineProperty(target, 'value', descriptor); }
                    catch (error) { console.log(error instanceof TypeError); }
                }
                try { Object.defineProperty(target, 'newProperty', {}); }
                catch (error) { console.log(error instanceof TypeError); }
            }
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat("true true true 0\ntrue\ntrue\ntrue\ntrue\n", 2)),
            TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    [Theory, ModeData]
    public void SealAndFreezeUpdateNativeAndCustomDescriptors(ExecutionMode mode)
    {
        const string source = """
            const O: any = Object, read: any = Object.getOwnPropertyDescriptor, original = O.keys;
            O.extra = 1;
            Object.seal(O);
            const keys = read(O, 'keys'), extra = read(O, 'extra');
            console.log(keys.value === original, keys.writable, keys.configurable,
                extra.value, extra.writable, extra.enumerable, extra.configurable);
            O.extra = 2;
            console.log(O.extra, delete O.extra, Object.seal(O) === O);
            Object.freeze(O);
            O.extra = 3;
            const frozen = read(O, 'keys');
            console.log(frozen.value === original, frozen.writable, frozen.configurable,
                O.extra, delete O.extra, Object.freeze(O) === O);
            """;
        Assert.Equal("true true false 1 true true false\n2 false true\ntrue false false 2 false true\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void RejectedWritesDoNotRestoreDeletedMethods(ExecutionMode mode)
    {
        const string source = """
            const O: any = Object, original = O.create, define: any = Object.defineProperty;
            function write(target: any, value: any) { 'use strict'; target['create'] = value; }
            delete O.create;
            Object.freeze(O);
            O.create = original;
            console.log(typeof O.create, Object.hasOwn(O, 'create'));
            try { write(O, original); } catch (error) { console.log(error instanceof TypeError); }
            try { define(O, 'create', { value: original }); }
            catch (error) { console.log(error instanceof TypeError); }
            console.log(typeof O.create, Object.hasOwn(O, 'create'), delete O.create);
            """;
        string output;
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, compiledOutput) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            output = compiledOutput;
        }
        else
        {
            output = TestHarness.Run(source, mode);
        }
        Assert.Equal("undefined false\ntrue\ntrue\nundefined false true\n", output);
    }

    [Theory, ModeData]
    public void FrozenOwnSettersKeepTheConstructorReceiver(ExecutionMode mode)
    {
        const string source = """
            const O: any = Object;
            let stored = 1;
            Object.defineProperty(O, 'value', {
                get: function() { return stored; },
                set: function(next: any) { console.log(this === O); stored = next; },
                configurable: true
            });
            Object.freeze(O);
            O.value = 7;
            const descriptor: any = Object.getOwnPropertyDescriptor(O, 'value');
            console.log(O.value, typeof descriptor.get, typeof descriptor.set, descriptor.configurable, delete O.value);
            """;
        string output;
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, compiledOutput) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            output = compiledOutput;
        }
        else
        {
            output = TestHarness.Run(source, mode);
        }
        Assert.Equal("true\n7 function function false false\n", output);
    }
}
