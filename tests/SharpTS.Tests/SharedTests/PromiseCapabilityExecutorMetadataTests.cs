using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class PromiseCapabilityExecutorMetadataTests
{
    [Theory, ModeData]
    public void CapabilityExecutorHasOwnMetadataAndHonorsOverrides(ExecutionMode mode)
    {
        Assert.Equal("2\ncustom custom\nfalse true\n", TestHarness.RunUnchecked("""
            let saved;
            function Other(executor) { saved = executor; executor(() => {}, () => {}); }
            Promise.resolve.call(Other, 1);
            console.log(saved.length);
            Object.defineProperty(saved, 'name', { value: 'custom', writable: true, enumerable: true, configurable: true });
            console.log(saved.name, Object.getOwnPropertyDescriptor(saved, 'name').value);
            let threw = false;
            try { new saved(); } catch (error) { threw = error instanceof TypeError; }
            console.log(Object.hasOwn(saved, 'prototype'), threw);
            """, mode));
    }
}
