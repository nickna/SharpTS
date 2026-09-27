using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class GenericPrivateOwnerTests
{
    [Theory]
    [InlineData("read() { const f = () => this.#read(); return f(); }", "console.log(new Box<number>(42).read());")]
    [InlineData("async read() { await Promise.resolve(0); return this.#read(); }", "const box = new Box<number>(42); async function main() { console.log(await box.read()); } main();")]
    [InlineData("*read() { yield this.#read(); }", "console.log(new Box<number>(42).read().next().value);")]
    [InlineData("read() { const f = () => { this.#value = 42; return this.#value; }; return f(); }", "console.log(new Box<number>(0).read());")]
    [InlineData("async read() { this.#value = await Promise.resolve(42); return this.#value; }", "const box = new Box<number>(0); async function main() { console.log(await box.read()); } main();")]
    [InlineData("*read() { this.#value = 42; yield this.#value; }", "console.log(new Box<number>(0).read().next().value);")]
    public void PrivateMethodThroughNestedEmission(string body, string invocation)
    {
        string source = "class Box<T> { #value; constructor(value: T) { this.#value = value; } #read() { return this.#value; } "
            + body + " } " + invocation;
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void ModulePrivateOwnersAreDistinct()
    {
        Dictionary<string, string> files = new()
        {
            ["a.ts"] = "class Box<T> { #value = 42; #read() { return this.#value; } read(other: any) { return other.#read(); } } export const a = new Box<number>();",
            ["b.ts"] = "class Box<T> { #value = 7; #read() { return this.#value; } read(other: any) { return other.#read(); } } export const b = new Box<string>();",
            ["main.ts"] = "import { a } from './a'; import { b } from './b'; console.log(a.read(a)); console.log(b.read(b)); try { a.read(b); } catch (e) { console.log('brand'); }"
        };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal("42\n7\nbrand\n", TestHarness.RunModules(files, "main.ts", ExecutionMode.Compiled));
    }

    [Fact]
    public void AwaitedPrivateArgumentsPreserveReceiverAndEarlierArguments()
    {
        const string source = """
            class Box<T> {
                #value;
                constructor(value: T) { this.#value = value; }
                #read(a: any, b: any = "default") { return this.#value + ":" + a + ":" + b; }
                async read() {
                    console.log(this.#read("first"));
                    return this.#read("first", await new Promise(resolve => setTimeout(() => resolve("later"), 1)));
                }
            }
            const box = new Box<string>("value");
            async function main() { console.log(await box.read()); }
            main();
            """;
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal("value:first:default\nvalue:first:later\n", output);
    }

    [Fact]
    public void PrivateBrandsSpanTypeArgumentsAndRemainLexical()
    {
        const string source = """
            class Box<T> {
                #value;
                constructor(value: T) { this.#value = value; }
                #read() { return this.#value; }
                readOther(other: any) { return other.#read(); }
                writeOther(other: any, value: any) { return other.#value = value; }
            }
            class Derived extends Box<number> { #read() { return "wrong"; } }
            const a = new Box<number>(42);
            const b = new Box<string>("ok");
            console.log(a.readOther(b));
            console.log(b.readOther(a));
            console.log(a.readOther(new Derived(7)));
            try { a.readOther({}); } catch (e) { console.log("brand"); }
            let order = "";
            function rhs() { order += "rhs"; return 1; }
            try { a.writeOther({}, rhs()); } catch (e) { console.log(order); }
            """;
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal("ok\n42\n7\nbrand\nrhs\n", output);
    }

    [Fact]
    public void PrivateMethodUsesConstructedGenericOwner()
    {
        const string source = """
            class Box<T> {
                #value;
                constructor(value: T) { this.#value = value; }
                #read() { return this.#value; }
                read() { return this.#read(); }
            }
            console.log(new Box<number>(42).read());
            console.log(new Box<string>("ok").read());
            """;
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal("42\nok\n", output);
    }
}
