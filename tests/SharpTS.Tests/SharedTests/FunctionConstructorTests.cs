using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class FunctionConstructorTests
{
    [Theory, ModeData]
    public void UnsupportedSource_IsRejectedAcrossCallAndConstructForms(ExecutionMode mode)
    {
        const string source = """
            const ctor: any = globalThis.Function;
            const bodies = ["return 42;", "return 43;", "return 42", " \treturn 43;\n",
                "", " ", "return @@@", "return this.x", "returnthis", "return\nthis",
                "return\rthis", "return\u2028this", "return\u2029this", "return this; extra()"];
            let rejected = 0;
            function check(action: any) {
                try { action(); }
                catch (error) {
                    if (error.message.includes("Dynamic Function() construction with source text is not supported"))
                        rejected++;
                }
            }
            for (const body of bodies) {
                check(() => Function(body));
                check(() => new Function(body));
                check(() => ctor(body));
                check(() => new ctor(body));
                check(() => globalThis.Function(body));
                check(() => new globalThis.Function(body));
            }
            check(() => ctor("x", "return this"));
            check(() => new ctor("", "return this"));
            check(() => ctor(undefined));
            check(() => ctor(null));
            check(() => ctor(42));
            console.log(rejected);
            """;

        Assert.Equal("89\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ReturnThis_UsesOneContractForEquivalentForms(ExecutionMode mode)
    {
        const string source = """
            const ctor: any = globalThis.Function;
            const bodies = ["return this", "return this;", " \nreturn\tthis ;\r\n", "\ufeffreturn  this\u00a0"];
            let passed = 0;
            for (const body of bodies) {
                if (Function(body)() === globalThis) passed++;
                if ((new Function(body))() === globalThis) passed++;
                if (ctor(body)() === globalThis) passed++;
                if ((new ctor(body))() === globalThis) passed++;
                if (globalThis.Function(body)() === globalThis) passed++;
                if ((new globalThis.Function(body))() === globalThis) passed++;
            }
            console.log(passed);
            """;

        Assert.Equal("24\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ConstructedFunctions_HaveOrdinaryReceiverAndIdentitySemantics(ExecutionMode mode)
    {
        const string source = """
            function make() {
                "use strict";
                const globalThis = { local: true };
                return Function("return this;");
            }
            const fn: any = make.call({ caller: true });
            const receiver: any = { fn };
            console.log(fn() === globalThis);
            console.log(receiver.fn() === receiver);
            console.log(fn.call(receiver) === receiver);
            console.log(fn.apply(receiver, []) === receiver);
            console.log(fn.bind(receiver)() === receiver);
            console.log(fn.call(null) === globalThis);
            console.log(fn.call(undefined) === globalThis);
            console.log(fn.name, fn.length, typeof fn);
            console.log(fn !== Function("return this;"));
            console.log(typeof new fn());
            const empty: any = Function();
            console.log(empty(), empty.name, empty.length);
            console.log((new Function())());
            const ctor: any = globalThis.Function;
            console.log(ctor()(), (new ctor())());
            """;

        var result = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\nanonymous 0 function\ntrue\nobject\nundefined anonymous 0\nundefined\nundefined undefined\n", result);
    }

    [Theory, ModeData]
    public void ShadowedFunction_IsInvokedNormally(ExecutionMode mode)
    {
        const string source = """
            function withParameter(Function: any) {
                console.log(Function("return this")());
                console.log((new Function("return this;"))());
                return () => Function("return this")();
            }
            function replacement(body: string): any {
                console.log(body);
                return () => 17;
            }
            console.log(withParameter(replacement)());
            {
                const Function: any = replacement;
                console.log(Function("return this")());
                console.log((new Function("return this"))());
            }
            """;

        Assert.Equal("return this\n17\nreturn this;\n17\nreturn this\n17\nreturn this\n17\nreturn this\n17\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ArgumentEvaluationAndSpreads_ArePreserved(ExecutionMode mode)
    {
        const string source = """
            const ctor: any = globalThis.Function;
            function arg(label: string): string { console.log(label); return label; }
            try { ctor(arg("a"), arg("b")); } catch { console.log("rejected"); }
            try { new Function(arg("c"), arg("d")); } catch { console.log("rejected"); }
            const args: any = ["return this;"];
            console.log(Function(...args)() === globalThis);
            console.log((new Function(...args))() === globalThis);
            console.log(ctor(...args)() === globalThis);
            console.log((new ctor(...args))() === globalThis);
            const none: any = [];
            console.log(Function(...none)());
            console.log((new ctor(...none))());
            """;

        Assert.Equal("a\nb\nrejected\nc\nd\nrejected\ntrue\ntrue\ntrue\ntrue\nundefined\nundefined\n",
            TestHarness.Run(source, mode));
    }

    [Fact]
    public void CompiledContract_WorksWithoutSharpTSRuntime()
    {
        Assert.Equal("true\nundefined\nrejected\n", TestHarness.RunCompiledStandalone("""
            const ctor: any = globalThis.Function;
            console.log((new ctor(" return this; "))() === globalThis);
            console.log(ctor()());
            try { ctor("return 42;"); } catch { console.log("rejected"); }
            """));
    }

    [Theory, ModeData]
    public void AsyncAndGeneratorConstruction_UseTheSameContract(ExecutionMode mode)
    {
        const string source = """
            const ctor: any = globalThis.Function;
            const root: any = globalThis;
            async function run() {
                const body = await Promise.resolve("return this;");
                console.log((new ctor(body))() === root);
                console.log(Function(body)() === root);
                try { new ctor("return 43;"); } catch { console.log("async rejected"); }
            }
            function* generate() {
                const body = yield "ready";
                yield (new ctor(body))() === root;
                try { new ctor("return 42;"); } catch { yield "generator rejected"; }
            }
            const values = generate();
            console.log(values.next().value);
            console.log(values.next("return this").value);
            console.log(values.next().value);
            run();
            """;

        Assert.Equal("ready\ntrue\ngenerator rejected\ntrue\ntrue\nasync rejected\n", TestHarness.Run(source, mode));
    }
}
