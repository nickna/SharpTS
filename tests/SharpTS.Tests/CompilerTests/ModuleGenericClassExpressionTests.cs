using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ModuleGenericClassExpressionTests
{
    [Theory]
    [InlineData("""
        let Box = class<T> {
            constructor(public value: T) {}
            read(): string { return "first:" + this.value; }
        };
        const First = Box;
        Box = (class<T> {
            constructor(public value: T) {}
            read(): string { return "second:" + this.value; }
        }) as any as typeof Box;
        console.log(new First(1).read());
        console.log(new Box(2).read());
        """, "first:1\nsecond:2\n", true)]
    [InlineData("""
        const Box = class<T> {
            constructor(public value: T) {}
        };
        {
            const Box = class<T> {
                constructor(public value: T) {}
            };
            console.log(new Box("inner").value);
        }
        console.log(new Box(42).value);
        """, "inner\n42\n", false)]
    [InlineData("""
        const Pair = class<T, U> {
            constructor(public first: T, public second: U) { console.log("ctor"); }
        };
        function numberArg(): number { console.log("first"); return 42; }
        function stringArg(): string { console.log("second"); return "hello"; }
        const pair = new Pair(numberArg(), stringArg());
        console.log(pair.first);
        console.log(pair.second);
        """, "first\nsecond\nctor\n42\nhello\n", false)]
    [InlineData("""
        const Box = class<T extends { name: string }> {
            constructor(public value: T) {}
        };
        const box = new Box({ name: "record" });
        console.log(box.value.name);
        """, "record\n", false)]
    public void RuntimeBindingsAndArgumentsArePreserved(string source, string expected, bool allowTypeErrors)
    {
        // Generic class reassignment currently fails compatibility checking,
        // even for an assertion to the original type. Exercise runtime binding
        // identity independently; the remaining cases require clean typing.
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts", allowTypeErrors));
        Assert.Equal(expected, TestHarness.RunModulesCompiled(files, "main.ts", allowTypeErrors: allowTypeErrors));
    }

    [Theory]
    [InlineData("import { Box as Alias } from './box';", "Alias")]
    [InlineData("import * as boxes from './box';", "boxes.Box")]
    public void ImportedGenericConstructorUsesItsRuntimeValue(string import, string callee)
    {
        var files = new Dictionary<string, string>
        {
            ["box.ts"] = """
                export const Box = class<T> { constructor(public value: T) {} };
                """,
            ["main.ts"] = $$"""
                {{import}}
                console.log(new {{callee}}<number>(42).value);
                """
        };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal("42\n", TestHarness.RunModulesCompiled(files, "main.ts"));
    }

    [Fact]
    public void SameNamedModuleBindingsKeepTheirRuntimeConstructors()
    {
        var files = new Dictionary<string, string>
        {
            ["left.ts"] = """
                const Box = class<T> {
                    constructor(public value: T) {}
                    read(): string { return "left:" + this.value; }
                };
                export const left = new Box(1);
                """,
            ["right.ts"] = """
                const Box = class<T> {
                    constructor(public value: T) {}
                    read(): string { return "right:" + this.value; }
                };
                export const right = new Box("two");
                """,
            ["main.ts"] = """
                import { left } from "./left";
                import { right } from "./right";
                console.log(left.read());
                console.log(right.read());
                """
        };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal("left:1\nright:two\n", TestHarness.RunModulesCompiled(files, "main.ts"));
    }

    [Theory]
    [InlineData("new Box(42)")]
    [InlineData("new Box<number>(42)")]
    public void ModuleInitializerClosesGenericClassExpression(string construction)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                const Box = class<T> {
                    constructor(private value: T) {}
                    get contents(): T { return this.value; }
                };
                const box = {{construction}};
                console.log(box.contents);
                """
        };

        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal("42\n", TestHarness.RunModulesCompiled(files, "main.ts"));
    }
}
