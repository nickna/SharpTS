using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class FrozenPropertyRedefinitionTests
{
    [Theory, ModeData]
    public void FrozenNullIsComparedAsAnActualValue(ExecutionMode mode)
    {
        const string source = """
            const target: any = { value: null };
            Object.freeze(target);
            try { Object.defineProperty(target, 'value', { value: 1 }); console.log('accepted'); }
            catch (error) { console.log(error instanceof TypeError, target.value === null); }
            console.log(Reflect.defineProperty(target, 'value', { value: null }),
                Reflect.defineProperty(target, 'value', { value: undefined }), target.value === null);
            const implicit: any = Object.defineProperty({}, 'value', { writable: false });
            console.log(Reflect.defineProperty(implicit, 'value', { value: undefined }),
                Reflect.defineProperty(implicit, 'value', { value: null }), implicit.value === undefined);
            """;
        Assert.Equal("true true\ntrue false true\ntrue false true\n", TestHarness.Run(source, mode));
    }

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
