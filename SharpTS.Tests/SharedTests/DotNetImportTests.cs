using SharpTS.Declaration;
using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for <c>dotnet:</c> import-scheme resolution (epic #1195): first-class imports of
/// .NET types with reflection-synthesized static types. Success scenarios run in BOTH
/// execution modes — the interpreter binds <c>DotNetClass</c> wrappers, compiled mode emits
/// direct external-interop IL — and must produce identical output.
/// </summary>
public class DotNetImportTests
{
    /// <summary>
    /// Type-checks a module set and returns error diagnostics. Statement-level type errors in
    /// module mode are COLLECTED (recovery), not thrown — the CLI prints them and refuses to
    /// run; tests must assert on the collected list (mirrors Program.cs RunModuleFile).
    /// </summary>
    private static List<Diagnostic> CheckErrors(Dictionary<string, string> files, string entryPoint)
    {
        var virtualBase = Path.Combine(Path.GetTempPath(), $"sharpts_dnimp_{Guid.NewGuid():N}");
        var virtualFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, content) in files)
            virtualFiles[Path.GetFullPath(Path.Combine(virtualBase, path.TrimStart('.', '/', '\\')))] = content;
        var entryPath = Path.GetFullPath(Path.Combine(virtualBase, entryPoint.TrimStart('.', '/', '\\')));

        var resolver = new ModuleResolver(entryPath, virtualFiles);
        var entryModule = resolver.LoadModule(entryPath);
        var allModules = resolver.GetModulesInOrder(entryModule);

        var checker = new TypeChecker();
        checker.CheckModules(allModules, resolver);
        return checker.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
    }

    #region Single-type form

    [Theory, ModeData]
    public void SingleTypeForm_ConstructChainAndProperty(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { StringBuilder } from "dotnet:System.Text.StringBuilder";
                const sb = new StringBuilder();
                sb.append("Hello").append(", ").append("dotnet:").append(42);
                console.log(sb.toString());
                console.log(sb.length);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Hello, dotnet:42\n16\n", output);
    }

    [Fact]
    public void SingleTypeForm_FluentChainIsStaticallyTyped()
    {
        // append() returns the synthesized StringBuilder type (not any) — assigning the
        // chain result to number must be a static type error.
        var errors = CheckErrors(new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { StringBuilder } from "dotnet:System.Text.StringBuilder";
                const sb = new StringBuilder();
                const n: number = sb.append("x");
                """
        }, "./main.ts");

        Assert.Contains(errors, d => d.Message.Contains("not assignable"));
    }

    [Fact]
    public void SingleTypeForm_PrimitiveReturnTypesArePrecise()
    {
        // toString(): string — assigning to number is a static error.
        var errors = CheckErrors(new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { StringBuilder } from "dotnet:System.Text.StringBuilder";
                const s: number = new StringBuilder().toString();
                """
        }, "./main.ts");

        Assert.Contains(errors, d => d.Message.Contains("not assignable"));
    }

    #endregion

    #region Namespace form, aliases, nested types

    [Theory, ModeData]
    public void NamespaceForm_ResolvesEachNamedImport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { Guid, Math as SysMath } from "dotnet:System";
                const g: Guid = Guid.newGuid();
                console.log(g.toString().length);
                console.log(SysMath.max(3, 9));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("36\n9\n", output);
    }

    [Theory, ModeData]
    public void AliasImport_BindsLocalName(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { StringBuilder as SB } from "dotnet:System.Text.StringBuilder";
                const sb: SB = new SB();
                sb.append("aliased");
                console.log(sb.toString());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("aliased\n", output);
    }

    [Theory, ModeData]
    public void StaticReadonlyField_Resolves(ExecutionMode mode)
    {
        // Guid.empty is a static FIELD (not property) — the member surface must include
        // public fields, and the runtime lookup resolves them the same way.
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { Guid } from "dotnet:System";
                console.log(Guid.empty.toString());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("00000000-0000-0000-0000-000000000000\n", output);
    }

    [Theory, ModeData]
    public void NestedType_ResolvesThroughDeclaringTypeSpecifier(ExecutionMode mode)
    {
        // System.Environment is a type; SpecialFolder is its nested enum — the namespace-form
        // name resolution falls back to Declaring+Nested.
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { SpecialFolder } from "dotnet:System.Environment";
                console.log(SpecialFolder.Desktop.toString());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Desktop\n", output);
    }

    [Theory, ModeData]
    public void EnumImport_MembersAndToString(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { DayOfWeek } from "dotnet:System";
                const d: DayOfWeek = DayOfWeek.Monday;
                console.log(d.toString());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Monday\n", output);
    }

    #endregion

    #region Cross-module and type identity

    [Theory, ModeData]
    public void CrossModule_InstanceFlowsBetweenModules(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./maker.ts"] = """
                import { StringBuilder } from "dotnet:System.Text.StringBuilder";
                export function makeSb(): StringBuilder {
                    const sb = new StringBuilder();
                    sb.append("from-maker");
                    return sb;
                }
                """,
            ["./main.ts"] = """
                import { StringBuilder } from "dotnet:System.Text.StringBuilder";
                import { makeSb } from "./maker";
                const sb: StringBuilder = makeSb();
                sb.append("+main");
                console.log(sb.toString());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("from-maker+main\n", output);
    }

    [Theory, ModeData]
    public void BothSpecifierForms_YieldTheSameType(ExecutionMode mode)
    {
        // The synthesized class is cached per CLR type, so the namespace form and the
        // single-type form produce the identical TypeInfo — values assign across modules.
        var files = new Dictionary<string, string>
        {
            ["./a.ts"] = """
                import { StringBuilder } from "dotnet:System.Text";
                export function make(): StringBuilder {
                    return new StringBuilder().append("same");
                }
                """,
            ["./main.ts"] = """
                import { StringBuilder } from "dotnet:System.Text.StringBuilder";
                import { make } from "./a";
                const sb: StringBuilder = make();
                console.log(sb.toString());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("same\n", output);
    }

    [Theory, ModeData]
    public void TypeOnlyImport_UsableInAnnotations(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./maker.ts"] = """
                import { StringBuilder } from "dotnet:System.Text.StringBuilder";
                export function makeSb(): StringBuilder {
                    return new StringBuilder().append("typed");
                }
                """,
            ["./main.ts"] = """
                import type { StringBuilder } from "dotnet:System.Text.StringBuilder";
                import { makeSb } from "./maker";
                const sb: StringBuilder = makeSb();
                console.log(sb.toString());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("typed\n", output);
    }

    #endregion

    #region Discovery-tool alignment

    [Theory, ModeData]
    public void DiscoveryToolImportLine_RoundTripsThroughThePipeline(ExecutionMode mode)
    {
        // The exact import line `--gen-decl` prints must load, type-check, and run —
        // the tool and the resolver share DotNetInteropClassifier, so they can never
        // disagree about importability (the #1192/#1193 lesson: round-trip, don't eyeball).
        var report = new DiscoveryGenerator().Generate("System.Text.StringBuilder");
        Assert.NotNull(report.Type?.ImportLine);

        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = report.Type!.ImportLine + """

                const sb = new StringBuilder();
                sb.append("round-trip");
                console.log(sb.toString());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("round-trip\n", output);
    }

    #endregion

    #region Error cases

    [Fact]
    public void UnknownName_ThrowsModuleError()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { Nope } from "dotnet:System.NoSuchNs";
                console.log(1);
                """
        };

        var ex = Assert.ThrowsAny<Exception>(() =>
            TestHarness.RunModules(files, "./main.ts", ExecutionMode.Interpreted));
        Assert.Contains("Module Error", ex.Message);
        Assert.Contains("System.NoSuchNs", ex.Message);
    }

    [Fact]
    public void SingleTypeForm_WrongName_ThrowsModuleError()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { Encoding } from "dotnet:System.Text.StringBuilder";
                console.log(1);
                """
        };

        var ex = Assert.ThrowsAny<Exception>(() =>
            TestHarness.RunModules(files, "./main.ts", ExecutionMode.Interpreted));
        Assert.Contains("exports only 'StringBuilder'", ex.Message);
    }

    [Fact]
    public void NamespaceStarImport_ThrowsModuleError()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as Text from "dotnet:System.Text";
                console.log(1);
                """
        };

        var ex = Assert.ThrowsAny<Exception>(() =>
            TestHarness.RunModules(files, "./main.ts", ExecutionMode.Interpreted));
        Assert.Contains("namespace imports", ex.Message);
    }

    [Fact]
    public void DefaultImport_ThrowsModuleError()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import StringBuilder from "dotnet:System.Text.StringBuilder";
                console.log(1);
                """
        };

        var ex = Assert.ThrowsAny<Exception>(() =>
            TestHarness.RunModules(files, "./main.ts", ExecutionMode.Interpreted));
        Assert.Contains("no default export", ex.Message);
    }

    [Fact]
    public void GenericSpecifier_ThrowsModuleError()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { List } from "dotnet:System.Collections.Generic.List<number>";
                console.log(1);
                """
        };

        var ex = Assert.ThrowsAny<Exception>(() =>
            TestHarness.RunModules(files, "./main.ts", ExecutionMode.Interpreted));
        Assert.Contains("Generic .NET types", ex.Message);
    }

    [Fact]
    public void RefStructType_ThrowsModuleError()
    {
        // TypedReference is a public, non-generic ref struct in the core library —
        // rejected with the classifier's reason, exactly like --gen-decl reports it.
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { TypedReference } from "dotnet:System.TypedReference";
                console.log(1);
                """
        };

        var ex = Assert.ThrowsAny<Exception>(() =>
            TestHarness.RunModules(files, "./main.ts", ExecutionMode.Interpreted));
        Assert.Contains(DotNetInteropClassifier.ReasonRefStruct, ex.Message);
    }

    [Fact]
    public void ReExportFrom_ThrowsModuleError()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                export { StringBuilder } from "dotnet:System.Text.StringBuilder";
                console.log(1);
                """
        };

        var ex = Assert.ThrowsAny<Exception>(() =>
            TestHarness.RunModules(files, "./main.ts", ExecutionMode.Interpreted));
        Assert.Contains("re-export", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImportRequire_ThrowsModuleError()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import sb = require("dotnet:System.Text.StringBuilder");
                console.log(1);
                """
        };

        var ex = Assert.ThrowsAny<Exception>(() =>
            TestHarness.RunModules(files, "./main.ts", ExecutionMode.Interpreted));
        Assert.Contains("named ESM import", ex.Message);
    }

    [Fact]
    public void NewOnStaticClass_IsATypeError()
    {
        // Static classes synthesize as abstract — `new` is rejected statically.
        var errors = CheckErrors(new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { Console } from "dotnet:System.Console";
                const c = new Console();
                """
        }, "./main.ts");

        Assert.Contains(errors, d => d.Message.Contains("abstract", StringComparison.OrdinalIgnoreCase));
    }

    #endregion
}
