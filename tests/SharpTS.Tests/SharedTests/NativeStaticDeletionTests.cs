using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class NativeStaticDeletionTests
{
    [Theory, ModeData]
    public void DeletionRemovesNativeStaticSlotAndAllowsRecreation(ExecutionMode mode)
    {
        const string source = """
            const constructors: any[] = [Array, Number, String];
            const members = ["from", "isFinite", "fromCharCode"];
            for (let i = 0; i < constructors.length; i++) {
                const target = constructors[i];
                const key = members[i];
                console.log(Object.hasOwn(target, key), delete target[key], Object.hasOwn(target, key), typeof target[key]);
                console.log(Object.getOwnPropertyDescriptor(target, key) === undefined);
                Object.defineProperty(target, key, { value: 42, writable: true, enumerable: true, configurable: true });
                console.log(Object.hasOwn(target, key), target[key], delete target[key], typeof target[key]);
            }
            console.log(delete (Number as any).MAX_VALUE, delete (Array as any).prototype);
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat("true true false undefined\ntrue\ntrue 42 true undefined\n", 3)) + "false false\n",
            TestHarness.Run(source, mode));
    }
}
