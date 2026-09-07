using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class PropertySemanticsTests
{
    [Theory, ModeData]
    public void NonWritableDescriptors_PreserveSameValueAcrossReceivers(ExecutionMode mode)
    {
        var source = """
            const identity: any = {};
            const targets: any[] = [{}, [], []];
            const keys: string[] = ["value", "0", "named"];
            for (let i = 0; i < targets.length; i++) {
                const target: any = targets[i];
                const key = keys[i];
                Object.defineProperty(target, key, { value: NaN });
                console.log(Reflect.defineProperty(target, key, { value: NaN }));
                console.log(Reflect.defineProperty(target, key, { value: 0 }));
                Object.defineProperty(target, "zero", { value: 0 });
                console.log(Reflect.defineProperty(target, "zero", { value: -0 }));
                Object.defineProperty(target, "identity", { value: identity });
                console.log(Reflect.defineProperty(target, "identity", { value: identity }));
                console.log(Reflect.defineProperty(target, "identity", { value: {} }));
            }
            """;

        Assert.Equal(string.Concat(Enumerable.Repeat("true\nfalse\nfalse\ntrue\nfalse\n", 3)),
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void PropertyKeys_DistinguishCanonicalIndicesFromOrdinaryNames(ExecutionMode mode)
    {
        var source = """
            const targets: any[] = [{}, []];
            const keys: string[] = ["01", "+1", " 1 ", "4294967295"];
            for (const target of targets) {
                for (const key of keys) {
                    Object.defineProperty(target, key, {
                        value: key, enumerable: true, configurable: true
                    });
                }
                target[2] = "2";
                target[0] = "0";
                console.log(Object.keys(target).join(","));
                console.log(target["01"], target["+1"], target[" 1 "], target["4294967295"]);
            }
            console.log(targets[1].length);
            """;

        Assert.Equal(string.Concat(Enumerable.Repeat(
            "0,2,01,+1, 1 ,4294967295\n01 +1  1  4294967295\n", 2)) + "3\n",
            TestHarness.Run(source, mode));
    }
}
