using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class AsyncLocalClassDeclarationTests
{
    [Theory, ModeData]
    public void ShadowedAsyncClasses_KeepSeparateBindings(ExecutionMode mode)
    {
        const string source = """
            async function run() {
                class C { static value = 5; }
                {
                    class C { static value = 7; }
                    await new Promise(r => setTimeout(r, 1));
                    console.log(C.value);
                }
                console.log(C.value);
            }
            run();
            """;
        Assert.Equal("7\n5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ShadowedGeneratorClasses_KeepSeparateBindings(ExecutionMode mode)
    {
        const string source = """
            function* run() {
                class C { static value = 5; }
                {
                    class C { static value = 7; }
                    yield 0;
                    console.log(C.value);
                }
                console.log(C.value);
            }
            const iterator = run();
            iterator.next();
            iterator.next();
            """;
        Assert.Equal("7\n5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ShadowedAsyncGeneratorClasses_KeepSeparateBindings(ExecutionMode mode)
    {
        const string source = """
            async function* run() {
                class C { static value = 5; }
                {
                    class C { static value = 7; }
                    yield 0;
                    console.log(C.value);
                }
                console.log(C.value);
            }
            const iterator = run();
            iterator.next().then(() => iterator.next());
            """;
        Assert.Equal("7\n5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DeclarationBeforeSuspension_PreservesClassBinding(ExecutionMode mode)
    {
        const string source = """
            async function run(){class C{static value=5;}await new Promise(r=>setTimeout(r,1));return C.value;}
            run().then(v=>console.log(v),e=>console.log("rejected",e.message));
            """;
        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DeclarationAfterSuspension_ResolvesClassBinding(ExecutionMode mode)
    {
        const string source = """
            async function run(){await new Promise(r=>setTimeout(r,1));class C{static value=5;}return C.value;}
            run().then(v=>console.log(v),e=>console.log("rejected",e.message));
            """;
        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DeclarationAfterIntrinsicAwait_ResolvesLocalClass(ExecutionMode mode)
    {
        const string source = """
            async function run(){await Promise.resolve(0);class C{static value=5;}return C.value;}
            run().then(v=>console.log(v));
            """;
        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DeclarationAfterIntrinsicAwait_DoesNotReject(ExecutionMode mode)
    {
        const string source = """
            async function run(){await Promise.resolve(0);class C{static value=5;}return C.value;}
            run().then(v=>console.log(v),e=>console.log("rejected",e.message));
            """;
        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DeclarationBeforeIntrinsicAwait_ResolvesLocalClass(ExecutionMode mode)
    {
        const string source = """
            async function run(){class C{static value=5;}const value=C.value;await Promise.resolve(0);return value;}
            run().then(v=>console.log(v));
            """;
        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }
}
