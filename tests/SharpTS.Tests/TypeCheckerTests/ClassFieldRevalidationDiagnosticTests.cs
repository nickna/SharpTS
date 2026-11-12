using System.Reflection;
using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ClassFieldRevalidationDiagnosticTests
{
    [Theory]
    [InlineData("class C", false)]
    [InlineData("const C = class", false)]
    [InlineData("class C", true)]
    [InlineData("const C = class", true)]
    public void StaticBlockRevalidationReportsAnInitializerDiagnosticOnce(string declaration, bool commonJs)
    {
        var statements = TestHarness.ParseOrThrow($$"""
            declare const required: { value: number };
            {{declaration}} {
                field = { value: 0, ...required };
                static {}
            }
            """);
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        IReadOnlyList<Diagnostic> diagnostics;
        if (commonJs)
        {
            string directory = Path.GetFullPath("class-revalidation-diagnostics");
            var module = new ParsedModule(Path.Combine(directory, "main.cjs"), statements) { IsCommonJs = true };
            checker.CheckModules([module], new ModuleResolver(directory));
            diagnostics = checker.GetDiagnostics();
        }
        else
            diagnostics = checker.CheckWithRecovery(statements).Diagnostics;

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("TS2783", diagnostic.TsCode);
        Assert.Equal(commonJs ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Theory]
    [InlineData("class C")]
    [InlineData("const C = class")]
    public void DuplicateInitializerErrorsDoNotHideLaterErrors(string declaration)
    {
        var statements = TestHarness.ParseOrThrow($$"""
            declare const required: { value: number };
            {{declaration}} {
                field = { value: 0, ...required };
                static {}
            }
            const wrong: number = 'wrong';
            """);
        var result = new TypeChecker(TypeCheckerOptions.Strict with { MaxErrors = 2 }).CheckWithRecovery(statements);

        Assert.Equal(["TS2783", "TS2322"], result.Diagnostics.Select(diagnostic => diagnostic.TsCode));
    }

    [Fact]
    public void RevalidationDoesNotSuppressSeparateLaterOccurrences()
    {
        var statements = TestHarness.ParseOrThrow("""
            declare const required: { value: number };
            class C { field = { value: 0, ...required }; static {} }
            const first = { value: 0, ...required }; const second = { value: 0, ...required };
            """);
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements).Diagnostics;

        Assert.Equal(3, diagnostics.Count);
        Assert.All(diagnostics, diagnostic => Assert.Equal("TS2783", diagnostic.TsCode));
        Assert.Equal(diagnostics[1], diagnostics[2]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RevalidationRestoresDuplicateSuppressionAfterSuccessOrFailure(bool suppressDuplicates, bool fail)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        var diagnostics = Assert.IsType<DiagnosticCollector>(typeof(TypeChecker)
            .GetField("_diagnostics", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(checker));
        diagnostics.SuppressDuplicates = suppressDuplicates;
        var statements = TestHarness.ParseOrThrow($$"""
            class C {
                static value: number | undefined;
                static { this.value = {{(fail ? "undefined" : "1")}}; }
                static read = this.value;
            }
            """);

        if (fail)
            Assert.Equal("TS2729", Assert.Throws<TypeCheckException>(() => checker.Check(statements)).Diagnostic.TsCode);
        else
            checker.Check(statements);

        Assert.Equal(suppressDuplicates, diagnostics.SuppressDuplicates);
    }
}
