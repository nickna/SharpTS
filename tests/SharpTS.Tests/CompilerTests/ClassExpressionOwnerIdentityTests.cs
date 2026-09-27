using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ClassExpressionOwnerIdentityTests
{
    [Theory]
    [InlineData("")]
    [InlineData("constructor() { super(); }")]
    public void SameNamedBaseBindingsAcrossModulesKeepTheirOwnParents(string constructor)
    {
        var files = new Dictionary<string, string>
        {
            ["left.ts"] = $$"""
                const Base = class Same { value: number = 1; read(): number { return this.value; } };
                export const Child = class extends Base { {{constructor}} read(): number { return super.read(); } };
                """,
            ["right.ts"] = $$"""
                const Base = class Same { value: number = 2; read(): number { return this.value; } };
                export const Child = class extends Base { {{constructor}} read(): number { return super.read(); } };
                """,
            ["main.ts"] = """
                import { Child as Left } from './left';
                import { Child as Right } from './right';
                console.log(new Left().read());
                console.log(new Right().read());
                """
        };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal("1\n2\n", TestHarness.RunModulesCompiled(files, "main.ts"));
    }

    [Fact]
    public void SameNamedBaseExpressionsKeepDerivedPropertyDispatch()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const Left = class Same { value: number = 1; };
                const Right = class Same { value: number = 2; };
                const LeftChild = class extends Left { read(): number { return this.value; } };
                const RightChild = class extends Right { read(): number { return this.value; } };
                console.log(new LeftChild().read());
                console.log(new RightChild().read());
                """
        };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal("1\n2\n", TestHarness.RunModulesCompiled(files, "main.ts"));
    }

    [Fact]
    public void NamedExpressionsKeepTheirObservableNames()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const Left = class Same {
                    value: number = 1;
                };
                const Right = class Same {
                    value: number = 2;
                };
                console.log((Left as any).name);
                console.log((Right as any).name);
                console.log(new Left().value);
                console.log(new Right().value);
                """
        };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal("Same\nSame\n1\n2\n", TestHarness.RunModulesCompiled(files, "main.ts"));
    }

    [Fact]
    public void SameNamedExpressionsInSeparateModulesKeepPrivateStorage()
    {
        var files = new Dictionary<string, string>
        {
            ["left.ts"] = "export const Box = class Same<T> { private value: number = 1; read(): number { return this.value; } };",
            ["right.ts"] = "export const Box = class Same<T> { private value: number = 2; read(): number { return this.value; } };",
            ["main.ts"] = """
                import { Box as Left } from './left';
                import { Box as Right } from './right';
                console.log(new Left<number>().read());
                console.log(new Right<string>().read());
                """
        };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal("1\n2\n", TestHarness.RunModulesCompiled(files, "main.ts"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<T>")]
    public void SameNamedExpressionsHaveIndependentPropertyDispatch(string parameters)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                const Left = class Same{{parameters}} {
                    value: number = 1;
                    read(): number { return this.value; }
                };
                const Right = class Same{{parameters}} {
                    value: number = 2;
                    read(): number { return this.value; }
                };
                console.log(new Left{{parameters.Replace("T", "number")}}().read());
                console.log(new Right{{parameters.Replace("T", "number")}}().read());
                """
        };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal("1\n2\n", TestHarness.RunModulesCompiled(files, "main.ts"));
    }
}
