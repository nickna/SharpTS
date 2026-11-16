using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GlobalIdentifierUpdateTests
{
    [Theory, ModeData]
    public void PrefixAndPostfixUpdatesPreserveResultsAndGlobalStorage(ExecutionMode mode)
    {
        const string source = """
            globalThis.counter = '1';
            console.log(counter++, globalThis.counter);
            console.log(++counter, globalThis.counter);
            console.log(counter--, globalThis.counter);
            console.log(--counter, globalThis.counter);
            function update(){'use strict'; return counter++;}
            console.log(update(), globalThis.counter);
            globalThis.big = 4n;
            console.log(big++, ++big, big--, --big, globalThis.big);
            """;
        Assert.Equal("1 2\n3 3\n3 2\n1 1\n1 2\n4n 6n 6n 4n 4n\n",
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void UpdateUsesOneGetAndSetAndHonorsGlobalDescriptors(ExecutionMode mode)
    {
        const string source = """
            let stored = 3;
            Object.defineProperty(globalThis, 'observed', {
                get(){console.log('get', this === globalThis); return stored;},
                set(v){console.log('set', this === globalThis, v); stored = v;}
            });
            console.log(observed++);
            console.log(stored);
            Object.defineProperty(globalThis, 'fixed', {value: 8, writable: false});
            console.log(fixed++, globalThis.fixed);
            try {(function(){'use strict'; ++fixed;})();}
            catch(e){console.log(e instanceof TypeError);}
            try {absent++;}
            catch(e){console.log(e instanceof ReferenceError);}
            console.log(Object.getOwnPropertyDescriptor(globalThis, 'absent') === undefined);
            """;
        Assert.Equal("get true\nset true 4\n3\n4\n8 8\ntrue\ntrue\ntrue\n",
            TestHarness.RunUnchecked(source, mode));
    }
}
