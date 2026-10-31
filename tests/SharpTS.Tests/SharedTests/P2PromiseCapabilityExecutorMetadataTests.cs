using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class P2PromiseCapabilityExecutorMetadataTests
{
    [Theory, ModeData]
    public void CapabilityExecutorOwnsAnonymousFunctionMetadataInCreationOrder(ExecutionMode mode)
    {
        const string source = """
            let saved: any;
            function Other(executor: any) { saved = executor; executor(() => {}, () => {}); }
            (Promise.resolve as any).call(Other, 1);
            const name: any = Object.getOwnPropertyDescriptor(saved, 'name');
            const length: any = Object.getOwnPropertyDescriptor(saved, 'length');
            console.log(saved.name === '', Object.getOwnPropertyNames(saved).join(','));
            console.log(name.value === '', name.writable, name.enumerable, name.configurable);
            console.log(length.writable, length.enumerable, length.configurable);
            console.log(Object.hasOwn(saved, 'prototype'));
            """;
        Assert.Equal("true length,name\ntrue false false true\nfalse false true\nfalse\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void InterpretedMetadataMutationDoesNotChangeCapabilityCallOrConstructorSemantics()
    {
        const string source = """
            let saved: any;
            function Other(executor: any) { saved = executor; executor(() => {}, () => {}); }
            (Promise.resolve as any).call(Other, 1);
            console.log(saved.length);
            saved.name = 'ignored';
            console.log(saved.name === '', delete saved.name, Object.hasOwn(saved, 'name'));
            Object.defineProperty(saved, 'name', {value: 'custom', writable: true, enumerable: true, configurable: true});
            saved.name = 'changed';
            console.log(saved.name, Object.getOwnPropertyNames(saved).join(','));
            try { saved(() => {}, () => {}); } catch (error: any) { console.log(error.name); }
            try { new saved(); } catch (error: any) { console.log(error.name); }
            """;
        Assert.Equal("2\ntrue true false\nchanged length,name\nTypeError\nTypeError\n", TestHarness.Run(source, ExecutionMode.Interpreted));
    }
}
