using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GenericConstructorCompatibilityTests
{
    public static IEnumerable<object[]> PositiveFixtures()
    {
        yield return ["public-pair.ts", "7\n"];
        yield return ["public-reassignment.ts", "first:1\nsecond:2\n"];
        yield return ["constrained-pair.ts", "ok\n"];
    }

    private static string Fixture(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SharpTS.sln"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root.FullName, "tests", "fixtures", "GenericConstructorCompatibility", file));
    }

    [Theory, MemberData(nameof(PositiveFixtures))]
    public void RetainedPositiveFixturesMatchBothEnginesAndSerializedOutput(string file, string expected)
    {
        var source = Fixture(file);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
        Assert.Equal(expected, TestHarness.RunCompiled(source));
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }

    [Theory]
    [InlineData("private-pair.ts")]
    [InlineData("static-pair.ts")]
    [InlineData("narrower-constraint.ts")]
    [InlineData("result-pair.ts")]
    public void RetainedNegativeFixturesRemainRejected(string file)
        => Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(TestHarness.ParseOrThrow(Fixture(file))));

    [Theory, ModeData]
    public void AliasesArgumentsReturnsAndInheritedConstructorsUseTheSelectedValue(ExecutionMode mode)
    {
        const string source = """
            class Base<T> { protected brand: number = 1; value: T;
                constructor(value: T) { this.value = value; }
                read(): T { return this.value; } }
            class Box<T> extends Base<T> { static tag: string = "box"; }
            class Other<U> extends Base<U> { static tag: string = "other"; }
            function identity(value: typeof Box): typeof Box { return value; }
            let Alias: typeof Box = Box;
            Alias = Alias;
            Alias = identity(Other);
            console.log(new Alias<number>(7).read(), Alias.tag);
            """;
        Assert.Equal("7 other\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void OptionalDefaultAndRestConstructorsPreserveRuntimeBinding(ExecutionMode mode)
    {
        const string source = """
            const Box = class<T = number> { value: T; constructor(value: T, ...rest: number[]) { this.value = value; }
                read(): T { return this.value; } };
            const Other = class<U = number> { value: U; constructor(value: U, count: number = 2) { this.value = value; }
                read(): U { return this.value; } };
            let Alias: typeof Box = Other;
            console.log(new Alias<number>(7, 2).read());
            """;
        Assert.Equal("7\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [InlineData("private value: number = 1;", "private value: number = 1;")]
    [InlineData("protected value: number = 1;", "protected value: number = 1;")]
    [InlineData("#value: number = 1;", "#value: number = 1;")]
    [InlineData("static private value: number = 1;", "static private value: number = 1;")]
    [InlineData("static protected value: number = 1;", "static protected value: number = 1;")]
    [InlineData("static #value: number = 1;", "static #value: number = 1;")]
    [InlineData("#read(): number { return 1; }", "#read(): number { return 1; }")]
    [InlineData("static value: number = 1;", "static value: string = 'bad';")]
    [InlineData("read(): number { return 1; }", "read(): string { return 'bad'; }")]
    public void SameNamedDeclarationsKeepOriginsAndCacheIsolation(string left, string right)
    {
        var source = $$"""
            const Left = class Same<T> { {{left}} };
            const Right = class Same<U> { {{right}} };
            let alias: typeof Left = Left;
            alias = alias;
            alias = Left;
            alias = Right;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModuleImportOrderPreservesStructuralAssignmentAndActualConstructor(bool reverse)
    {
        var files = new Dictionary<string, string>
        {
            ["left.ts"] = "export const Box = class Same<T> { value: T; constructor(value: T) { this.value = value; } };",
            ["right.ts"] = "export const Box = class Same<U> { value: U; constructor(value: U) { this.value = value; console.log('right'); } };",
            ["main.ts"] = (reverse
                ? "import { Box as Right } from './right'; import { Box as Left } from './left';"
                : "import { Box as Left } from './left'; import { Box as Right } from './right';") + """
                let Alias: typeof Left = Left;
                Alias = Alias;
                Alias = Right;
                console.log(new Alias<number>(7).value);
                """
        };
        Assert.Equal("right\n7\n", TestHarness.RunModules(files, "main.ts", ExecutionMode.Interpreted));
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal("right\n7\n", TestHarness.RunModules(files, "main.ts", ExecutionMode.Compiled));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModuleImportOrderDoesNotErasePrivateOrigins(bool reverse)
    {
        var files = new Dictionary<string, string>
        {
            ["left.ts"] = "export const Box = class Same<T> { private value: number = 1; };",
            ["right.ts"] = "export const Box = class Same<U> { private value: number = 1; };",
            ["main.ts"] = (reverse
                ? "import { Box as Right } from './right'; import { Box as Left } from './left';"
                : "import { Box as Left } from './left'; import { Box as Right } from './right';")
                + "let Alias: typeof Left = Left; Alias = Alias; Alias = Right;"
        };
        Assert.Throws<TypeCheckDiagnosticException>(() => TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
    }

    [Theory, ModeData]
    public void CommonBasePrivateBrandsRemainCompatible(ExecutionMode mode)
        => Assert.Equal("7\n", TestHarness.Run("""
            class Base<T> { #brand: number = 1; value: T;
                constructor(value: T) { this.value = value; } }
            class Left<T> extends Base<T> {}
            class Right<U> extends Base<U> {}
            const Alias: typeof Left = Right;
            console.log(new Alias<number>(7).value);
            """, mode));

    [Theory]
    [InlineData("constructor(value: T) {}", "constructor(value: U, extra: number) {}")]
    [InlineData("constructor(value: T, ...rest: number[]) {}", "constructor(value: U, ...rest: string[]) {}")]
    public void IncompatibleRequiredAndRestParametersAreRejected(string left, string right)
        => Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(TestHarness.ParseOrThrow($$"""
            class Left<T> { {{left}} }
            class Right<U> { {{right}} }
            const Alias: typeof Left = Right;
            """)));
}
