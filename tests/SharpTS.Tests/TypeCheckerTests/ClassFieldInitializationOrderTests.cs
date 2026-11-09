using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.IntegrationTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ClassFieldInitializationOrderTests
{
    [Theory]
    [InlineData("class C", "z = this.y;", "y = 1;")]
    [InlineData("const C = class", "z = (this).y;", "y: number = 1;")]
    [InlineData("class C", "static z = this.y;", "static y = 1;")]
    [InlineData("class C", "static z = C.y;", "static y = 1;")]
    [InlineData("class C", "z = this.#y;", "#y = 1;")]
    public void DirectReadOfLaterFieldReportsAnInitializationError(string declaration, string initializer, string field)
    {
        var statements = TestHarness.ParseOrThrow($$"""
            {{declaration}} {
                {{initializer}}
                {{field}}
            }
            """);
        var diagnostic = Assert.Single(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements).Diagnostics);
        Assert.Equal("TS2729", diagnostic.TsCode);
        Assert.Equal(2, diagnostic.Line);
    }

    [Fact]
    public void DirectReadOfEarlierConstructorAssignedFieldReportsAnInitializationError()
    {
        var statements = TestHarness.ParseOrThrow("""
            class C {
                y: number;
                z = this.y;
                constructor() { this.y = 1; }
            }
            """);
        var diagnostic = Assert.Single(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements).Diagnostics);
        Assert.Equal("TS2729", diagnostic.TsCode);
        Assert.Equal(3, diagnostic.Line);
    }

    [Theory]
    [InlineData("class C", "value", "")]
    [InlineData("const C = class", "#value", "")]
    [InlineData("class C", "value", "static { if (false) this.value = 1; }")]
    [InlineData("class C", "value", "static { if (flag) this.value = 1; }")]
    [InlineData("class C", "value", "static { const assign = () => { this.value = 1; }; }")]
    public void EarlierUninitializedStaticFieldReportsAnInitializationError(
        string declaration, string field, string earlierBlock)
    {
        var statements = TestHarness.ParseOrThrow($$"""
            declare const flag: boolean;
            {{declaration}} {
                static {{field}}: number;
                {{earlierBlock}}
                static read = this.{{field}};
                static { this.{{field}} = 3; }
            }
            """);
        var diagnostic = Assert.Single(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements).Diagnostics);
        Assert.Equal("TS2729", diagnostic.TsCode);
        Assert.Equal(5, diagnostic.Line);
    }

    [Theory]
    [InlineData("class C", "value", "this.value = 1;")]
    [InlineData("class C", "value", "if (true) this.value = 1;")]
    [InlineData("class C", "value", "if (flag) this.value = 1; else this.value = 2;")]
    [InlineData("const C = class", "#value", "this.#value = 1;")]
    public void EarlierStaticBlockCanInitializeAField(string declaration, string field, string assignments)
    {
        var statements = TestHarness.ParseOrThrow($$"""
            declare const flag: boolean;
            {{declaration}} {
                static {{field}}: number;
                static { {{assignments}} }
                static read = this.{{field}};
            }
            """);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements).Diagnostics);
    }

    [Fact]
    public void ScriptStaticBlockAssignmentUsesTheClassReceiverWithDefaultLibraries()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", """
            class C {
                static value: number;
                static { this.value = 1; }
                static read = this.value;
            }
            """);
        var result = CliTestHelper.RunCli($"--no-tsconfig --strict --noEmit \"{path}\"", directory.Path);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
    }

    [Fact]
    public void EarlierOptionalAndDeferredFieldsRemainReadable()
    {
        var statements = TestHarness.ParseOrThrow("""
            class C {
                first = 1;
                earlier = this.first;
                deferred = () => this.later;
                optional = this.maybe;
                asserted!: number;
                assumed = this.asserted;
                nested = class { read() { return C.staticLater; } };
                later = 2;
                maybe?: number;
                static staticLater = 3;
            }
            """);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements).Diagnostics);
    }
}
