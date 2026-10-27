using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NativePromiseSuperclassAliasTests
{
    [Theory, ModeData]
    public void NativeAliasSupportsImplicitAndExplicitSuperConstruction(ExecutionMode mode)
    {
        const string source = """
            const NativePromise: any = Promise;
            class C extends NativePromise {}
            console.log(typeof C, Object.getPrototypeOf(C) === Promise);
            const first: any = new C((resolve: any) => resolve(42));
            first.then((value: any) => console.log(value));
            class D extends NativePromise {
                constructor(executor: any) { super(executor); }
            }
            const value: any = new D((resolve: any) => resolve(43));
            console.log(value instanceof D, value instanceof Promise);
            value.then((value: any) => console.log(value));
            """;
        Assert.Equal("function true\ntrue true\n42\n43\n", TestHarness.Run(source, mode));
    }
}
