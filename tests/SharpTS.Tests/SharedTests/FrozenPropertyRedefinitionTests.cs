using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class FrozenPropertyRedefinitionTests
{
    [Theory, ModeData]
    public void FrozenExistingPropertiesAcceptOnlyCompatibleDefinitions(ExecutionMode mode)
    {
        const string source = """
            const targets: any[] = [{}, Object];
            for (const target of targets) {
                let reads = 0;
                const getter = function() { reads++; return 2; };
                Object.defineProperty(target, "value", { value: 7, writable: true, configurable: true });
                Object.defineProperty(target, "accessor", { get: getter, configurable: true });
                Object.freeze(target);
                console.log(Object.defineProperty(target, "value", {}) === target);
                console.log(Object.defineProperty(target, "value", { value: 7 }) === target);
                console.log(Object.defineProperty(target, "accessor", { get: getter }) === target, reads);
                try { Object.defineProperty(target, "value", { value: 8 }); }
                catch (error: any) { console.log(error instanceof TypeError, target.value); }
                try { Object.defineProperty(target, "added", { value: 9 }); }
                catch (error: any) { console.log(error instanceof TypeError); }
            }
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat("true\ntrue\ntrue 0\ntrue 7\ntrue\n", 2)), TestHarness.Run(source, mode));
    }
}
