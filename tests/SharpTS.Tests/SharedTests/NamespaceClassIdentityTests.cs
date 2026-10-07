using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NamespaceClassIdentityTests
{
    private static string Fixture(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SharpTS.sln"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root.FullName, "tests", "fixtures", "NamespaceClassIdentity", file));
    }

    [Theory]
    [InlineData("siblings.ts")]
    [InlineData("siblings-nongeneric.ts")]
    public void RetainedSiblingFixturesPreserveOwners(string file)
    {
        var source = Fixture(file);
        Assert.Equal("1 2 9\n", TestHarness.RunInterpreted(source));
        Assert.Equal("1 2 9\n", TestHarness.RunCompiled(source));
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal("1 2 9\n", output);
    }

    [Theory]
    [InlineData("main-left.ts")]
    [InlineData("main-right.ts")]
    public void RetainedModuleFixturesPreserveOwnersInBothImportOrders(string entry)
    {
        var files = new Dictionary<string, string>
        {
            ["left.ts"] = Fixture("left.ts"),
            ["right.ts"] = Fixture("right.ts"),
            [entry] = Fixture(entry) + """
                const L = Left.Box, R = Right.Box;
                console.log(L === R, L.name, R.name);
                console.log(new L<number>().read(), new R<string>().read());
                """
        };
        const string expected = "1 2\nfalse Box Box\n1 2\n";
        Assert.Equal(expected, TestHarness.RunModules(files, entry, ExecutionMode.Interpreted));
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, entry));
        Assert.Equal(expected, TestHarness.RunModules(files, entry, ExecutionMode.Compiled));
    }

    [Theory, ModeData]
    public void NestedReopenedNamespacesKeepPrivateStorageAndBareClassReferences(ExecutionMode mode)
    {
        const string source = """
            namespace Left {
                export let value = 1;
                export class Box<T> {
                    #private: number = 10;
                    value: T;
                    constructor(value: T) { this.value = value; }
                    read() { return value + this.#private; }
                    again(value: T) { return new Box<T>(value); }
                }
                export namespace Nested { export class Box { read() { return 3; } } }
            }
            namespace Left { export class Other extends Box<number> { read() { return super.read() + 1; } } }
            namespace Right {
                export let value = 2;
                export class Box<T> {
                    #private: number = 20;
                    value: T;
                    constructor(value: T) { this.value = value; }
                    read() { return value + this.#private; }
                    again(value: T) { return new Box<T>(value); }
                }
            }
            class Box { read() { return 9; } }
            const l = new Left.Box<number>(7), r = new Right.Box<string>("ok");
            console.log(l.read(), r.read(), l.again(8).read(), r.again("next").read());
            console.log(l.value, r.value, new Left.Nested.Box().read(), new Left.Other(5).read(), new Box().read());
            """;
        Assert.Equal("11 22 11 22\n7 ok 3 12 9\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ModuleMemberBodiesResolveTheirOwnNamespaceInBothImportOrders(ExecutionMode mode)
    {
        foreach (var reverse in new[] { false, true })
        {
            var files = new Dictionary<string, string>
            {
                ["left.ts"] = """
                    export namespace Library {
                        export let value = 1;
                        export class Box<T> {
                            #value = 10;
                            read() { return value + this.#value; }
                            again() { return new Box<T>(); }
                            static read() { return value; }
                        }
                    }
                    """,
                ["right.ts"] = """
                    export namespace Library {
                        export let value = 2;
                        export class Box<T> {
                            #value = 20;
                            read() { return value + this.#value; }
                            again() { return new Box<T>(); }
                            static read() { return value; }
                        }
                    }
                    """,
                ["main.ts"] = (reverse
                    ? "import { Library as R } from './right'; import { Library as L } from './left';"
                    : "import { Library as L } from './left'; import { Library as R } from './right';") + """
                    const l = new L.Box<number>(), r = new R.Box<string>();
                    console.log(l.again().read(), r.again().read(), L.Box.read(), R.Box.read());
                    """
            };
            Assert.Equal("11 22 1 2\n", TestHarness.RunModules(files, "main.ts", mode));
        }
    }

    [Theory]
    [InlineData("private value = 1;")]
    [InlineData("protected value = 1;")]
    [InlineData("#value = 1;")]
    public void SiblingNamespacesKeepDistinctPrivateOrigins(string member)
        => Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(TestHarness.ParseOrThrow($$"""
            namespace Left { export class Box<T> { {{member}} } }
            namespace Right { export class Box<T> { {{member}} } }
            let alias: typeof Left.Box = Left.Box;
            alias = Right.Box;
            """)));

    [Theory, ModeData]
    public void TypedSiblingMethodsAndStaticMembersKeepDeclarationMetadata(ExecutionMode mode)
        => Assert.Equal("7 ok\n11 22\n", TestHarness.Run("""
            namespace Left { export class Box {
                static value: number = 11;
                value: number; constructor(value: number) { this.value = value; }
                read(): number { return this.value; }
                get result(): number { return this.value; }
            } }
            namespace Right { export class Box {
                static value: number = 22;
                value: string; constructor(value: string) { this.value = value; }
                read(): string { return this.value; }
                get result(): string { return this.value; }
            } }
            const l: Left.Box = new Left.Box(7), r: Right.Box = new Right.Box("ok");
            console.log(l.result, r.read());
            console.log(Left.Box.value, Right.Box.value);
            """, mode));
}
