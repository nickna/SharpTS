using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class AsyncLocalClassDeclarationTests
{
    [Theory, ModeData]
    public void AwaitedInstanceGetterKey_IsReadableByName(ExecutionMode mode)
    {
        const string source = """
            async function run() {
                class C { get [await new Promise(r => setTimeout(() => r("value"), 1))]() { return 5; } }
                const c: any = new C();
                console.log(c.value);
            }
            run().then(() => {}, e => console.log("rejected", e.message));
            """;
        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AwaitedStaticSetterKey_IsWritableByName(ExecutionMode mode)
    {
        const string source = """
            async function run() {
                class C { static set [await new Promise(r => setTimeout(() => r("value"), 1))](v) { console.log(v); } }
                const cls: any = C;
                cls.value = 5;
            }
            run().then(() => {}, e => console.log("rejected", e.message));
            """;
        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void RejectedFieldKey_IsCaughtWithoutRunningStaticInitializer(ExecutionMode mode)
    {
        const string source = """
            async function run() {
                try {
                    class C {
                        static [await new Promise((r, j) => setTimeout(() => j(new Error("key")), 1))] = console.log("bad");
                    }
                } catch (e) { console.log(e.message); }
                class D { static value = 5; }
                console.log(D.value);
            }
            run().then(() => {}, e => console.log("outer", e.message));
            """;
        Assert.Equal("key\n5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AwaitedHeritageExpression_PreservesSideEffectsAndBaseMembers(ExecutionMode mode)
    {
        const string source = """
            class B { static value = 5; }
            async function run() {
                class C extends (await new Promise(r => setTimeout(() => { console.log("base"); r(0); }, 1)), B) {}
                const cls: any = C;
                return cls.value;
            }
            run().then(v => console.log(v), e => console.log("rejected", e.message));
            """;
        Assert.Equal("base\n5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncArrowClassExpression_AwaitsHeritageBeforeConstruction(ExecutionMode mode)
    {
        const string source = """
            class B { value() { return 5; } }
            const run = async () => {
                const C = class extends (await new Promise(r => setTimeout(() => { console.log("base"); r(0); }, 1)), B) {};
                const c: any = new C();
                return c.value();
            };
            run().then(v => console.log(v), e => console.log("rejected", e.message));
            """;
        Assert.Equal("base\n5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AwaitedStaticFieldKey_IsEvaluatedAtDefinition(ExecutionMode mode)
    {
        const string source = """
            async function run() {
                class C { static [await new Promise(r => setTimeout(() => r("value"), 1))] = 5; }
                const cls: any = C;
                return cls.value;
            }
            run().then(v => console.log(v), e => console.log("rejected", e.message));
            """;
        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AwaitedInstanceFieldKey_IsEvaluatedOnceBeforeConstruction(ExecutionMode mode)
    {
        const string source = """
            async function run() {
                let calls = 0;
                class C {
                    [await new Promise(r => setTimeout(() => { calls++; r("value"); }, 1))] = 5;
                }
                const a: any = new C();
                const b: any = new C();
                console.log(calls, a.value, b.value);
            }
            run().then(() => {}, e => console.log("rejected", e.message));
            """;
        Assert.Equal("1 5 5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AwaitedClassExpressionFieldKey_PreservesSymbolIdentity(ExecutionMode mode)
    {
        const string source = """
            async function run() {
                const key = Symbol("key");
                const C = class { static [await new Promise(r => setTimeout(() => r(key), 1))] = 5; };
                const cls: any = C;
                console.log(cls[key]);
            }
            run().then(() => {}, e => console.log("rejected", e.message));
            """;
        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AwaitedAccessorAndMethodKeys_PreserveSourceOrder(ExecutionMode mode)
    {
        const string source = """
            async function run() {
                class C {
                    static get [await new Promise(r => setTimeout(() => { console.log("first"); r("a"); }, 1))]() { return 2; }
                    static [await new Promise(r => setTimeout(() => { console.log("second"); r("b"); }, 1))]() { return 3; }
                }
                const cls: any = C;
                return cls.a + cls.b();
            }
            run().then(v => console.log(v), e => console.log("rejected", e.message));
            """;
        Assert.Equal("first\nsecond\n5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AwaitedAccessorKey_IsEvaluatedInAsyncFunction(ExecutionMode mode)
    {
        const string source = """
            async function run() {
                class C { static get [await new Promise(r => setTimeout(() => r("value"), 1))]() { return 5; } }
                const cls: any = C;
                return cls.value;
            }
            run().then(v => console.log(v), e => console.log("rejected", e.message));
            """;
        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AwaitedMethodKey_IsEvaluatedInAsyncGenerator(ExecutionMode mode)
    {
        const string source = """
            async function* run() {
                class C { static [await new Promise(r => setTimeout(() => r("value"), 1))]() { return 5; } }
                const cls: any = C;
                yield cls.value();
            }
            run().next().then(v => console.log(v.value), e => console.log("rejected", e.message));
            """;
        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void MultipleAwaitedMethodKeys_PreserveEarlierKeys(ExecutionMode mode)
    {
        const string source = """
            async function run() {
                class C {
                    static [await new Promise(r => setTimeout(() => r("a"), 1))]() { return 2; }
                    static [await new Promise(r => setTimeout(() => r("b"), 1))]() { return 3; }
                }
                const cls: any = C;
                return cls.a() + cls.b();
            }
            run().then(v => console.log(v), e => console.log("rejected", e.message));
            """;
        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AwaitedMethodKey_IsEvaluatedInAsyncFunction(ExecutionMode mode)
    {
        const string source = """
            async function run() {
                class C { static [await new Promise(r => setTimeout(() => r("value"), 1))]() { return 5; } }
                const cls: any = C;
                return cls.value();
            }
            run().then(v => console.log(v), e => console.log("rejected", e.message));
            """;
        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AwaitedClassExpressionMethodKey_IsEvaluatedInAsyncArrow(ExecutionMode mode)
    {
        const string source = """
            const run = async () => {
                const C = class { static [await new Promise(r => setTimeout(() => r("value"), 1))]() { return 5; } };
                const cls: any = C;
                return cls.value();
            };
            run().then(v => console.log(v), e => console.log("rejected", e.message));
            """;
        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ArrowDeclarationBeforeSuspension_PreservesClassBinding(ExecutionMode mode)
    {
        const string source = """
            const run = async () => {
                class C { static value = 5; }
                await new Promise(r => setTimeout(r, 1));
                return C.value;
            };
            run().then(v => console.log(v), e => console.log("rejected", e.message));
            """;
        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

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
