using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InheritedPrivateDispatchTests
{
    [Theory, ModeData]
    public void ImplicitDerivedConstructorInitializesAncestorPrivateStorageOnce(ExecutionMode mode)
    {
        const string source = """
            let calls = 0;
            class Base {
                #value = ++calls;
                #read() { return this.#value; }
                read() { return this.#read(); }
            }
            class Derived extends Base {}
            console.log(new Derived().read(), calls);
            """;
        Assert.Equal("1 1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ModuleLocalSameNamedOwnersRejectEachOthersPrivateMethods(ExecutionMode mode)
    {
        Dictionary<string, string> files = new()
        {
            ["a.ts"] = "class Box<T> { #read() { return 'a'; } read(other: any) { return other.#read(); } } export const a = new Box<number>();",
            ["b.ts"] = "class Box<T> { #read() { return 'b'; } read(other: any) { return other.#read(); } } export const b = new Box<string>();",
            ["main.ts"] = "import { a } from './a'; import { b } from './b'; console.log(a.read(a), b.read(b)); try { a.read(b); } catch (e) { console.log(e instanceof TypeError); }"
        };
        Assert.Equal("a b\ntrue\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    // Retained #1854 source; #1902 independently covers interpreted dispatch.
    [Theory, ModeData]
    public void PrivateBrandsSpanTypeArgumentsAndRemainLexical(ExecutionMode mode)
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
        Assert.Equal("ok\n42\n7\nbrand\nrhs\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SameSpelledDerivedPrivateMembersDoNotReplaceBaseMembers(ExecutionMode mode)
    {
        const string source = """
            class Base {
                #tag = "base";
                #read() { return this.#tag; }
                read() { return this.#read(); }
                readOther(other: any) { return other.#read(); }
            }
            class Derived extends Base {
                #tag = "derived";
                #read() { return this.#tag; }
                readOwn() { return this.#read(); }
            }
            class Other { #read() { return "other"; } }
            const value = new Derived();
            console.log(value.read(), value.readOwn());
            try { value.readOther(new Other()); } catch (e) { console.log(e instanceof TypeError); }
            """;
        Assert.Equal("base derived\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InheritedPrivateDispatchSurvivesClosuresAndSuspension(ExecutionMode mode)
    {
        const string source = """
            class Base {
                #read() { return "base"; }
                closure() { const read = () => this.#read(); return read(); }
                async readAsync() { await Promise.resolve(0); return this.#read(); }
                *values() { yield this.#read(); }
                async *asyncValues() { await Promise.resolve(0); yield this.#read(); }
            }
            class Middle extends Base {}
            class Derived extends Middle { #read() { return "derived"; } }
            const value = new Derived();
            console.log(value.closure(), value.values().next().value);
            async function main() {
                console.log(await value.readAsync());
                console.log((await value.asyncValues().next()).value);
            }
            main();
            """;
        Assert.Equal("base base\nbase\nbase\n", TestHarness.Run(source, mode));
    }
}
