using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class FrozenClassConstructorAssignmentTests
{
    [Theory, ModeData]
    public void FrozenDataWritesFailWithoutChangingTheValue(ExecutionMode mode)
    {
        const string source = """
            function write(target: any, key: string, value: any) { 'use strict'; target[key] = value; }
            class C {}
            const owner: any = C;
            Object.preventExtensions(owner);
            try { write(owner, 'missing', 2); } catch (error) { console.log(error instanceof TypeError); }
            console.log(Object.isExtensible(owner), Object.hasOwn(owner, 'missing'));
            class F {}
            const frozen: any = F;
            frozen.value = 1;
            Object.freeze(frozen);
            try { write(frozen, 'value', 2); } catch (error) { console.log(error instanceof TypeError); }
            console.log(Object.isFrozen(frozen), frozen.value);
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
        Assert.Equal("true\nfalse false\ntrue\ntrue 1\n", output);
    }

    [Theory, ModeData]
    public void SloppyFrozenDataWriteLeavesTheValueUnchanged(ExecutionMode mode)
    {
        const string source = """
            class C {}
            const target: any = C;
            target.value = 1;
            Object.freeze(target);
            target.value = 2;
            console.log(Object.isFrozen(target), target.value);
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
        Assert.Equal("true 1\n", output);
    }

    [Theory, ModeData]
    public void FrozenAccessorsRetainTheConstructorReceiver(ExecutionMode mode)
    {
        const string source = """
            function write(target: any, key: string, value: any) { 'use strict'; target[key] = value; }
            class Base {}
            class Derived extends Base {}
            let calls = 0;
            Object.defineProperty(Base, 'inherited', {
                set(value: any) { calls++; console.log(this === Derived, value); }, configurable: true
            });
            Object.defineProperty(Derived, 'own', {
                set(value: any) { calls++; console.log(this === Derived, value); }, configurable: true
            });
            Object.freeze(Derived);
            write(Derived, 'inherited', 7);
            write(Derived, 'own', 8);
            console.log(calls, Object.hasOwn(Derived, 'inherited'));
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
        Assert.Equal("true 7\ntrue 8\n2 false\n", output);
    }
}
