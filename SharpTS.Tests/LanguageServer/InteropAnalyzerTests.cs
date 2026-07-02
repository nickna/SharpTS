using SharpTS.Compilation;
using SharpTS.Diagnostics;
using SharpTS.LanguageServer;
using SharpTS.LanguageServer.Services;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.LanguageServer;

/// <summary>
/// Covers the @DotNetType interop analyzer (re-scoped Phase 1 of the language server):
/// each tier fires on a real interop mistake, and valid bindings produce zero diagnostics
/// on both the in-process and the AssemblyReferenceLoader (production) resolver.
/// </summary>
public class InteropAnalyzerTests
{
    private static List<Diagnostic> Analyze(string src, Func<string, Type?>? resolve = null)
    {
        var tokens = new Lexer(src).ScanTokens();
        var parsed = new Parser(tokens, DecoratorMode.Stage3).Parse();
        Assert.True(parsed.IsSuccess,
            "parse failed: " + string.Join("; ", parsed.Diagnostics.Select(d => d.Message)));
        return new InteropAnalyzer(resolve).Analyze(parsed.Statements);
    }

    private const string AppDomainBinding =
        "@DotNetType(\"System.AppDomain\")\n" +
        "declare class AppDomain {\n" +
        "  static readonly currentDomain: AppDomain;\n" +
        "  addEventListener(name: string, handler: (s: any, a: any) => void): void;\n" +
        "}\n";

    [Fact]
    public void Tier1_UnknownType()
    {
        var d = Analyze("@DotNetType(\"System.Text.StringBulder\")\ndeclare class SB { append(v: string): SB; }");
        Assert.Single(d);
        Assert.Contains("not found", d[0].Message);
    }

    [Fact]
    public void Tier2_UnknownMember()
    {
        var d = Analyze("@DotNetType(\"System.Text.StringBuilder\")\ndeclare class SB { appendx(v: string): SB; }");
        Assert.Single(d);
        Assert.Contains("appendx", d[0].Message);
    }

    [Fact]
    public void Tier3a_BadOverloadHint()
    {
        var d = Analyze("@DotNetType(\"System.Convert\")\ndeclare class Conv { @DotNetOverload(\"flot\") static toInt32(value: number): number; }");
        Assert.Single(d);
        Assert.Contains("flot", d[0].Message);
    }

    [Fact]
    public void Tier3b_StaticInstanceMismatch()
    {
        var d = Analyze("@DotNetType(\"System.Text.StringBuilder\")\ndeclare class SB { static append(value: string): SB; }");
        Assert.Single(d);
        Assert.Contains("not static as declared", d[0].Message);
    }

    [Fact]
    public void Tier3c_NoConstructor()
    {
        var d = Analyze("@DotNetType(\"System.Convert\")\ndeclare class Conv { constructor(); }");
        Assert.Single(d);
        Assert.Contains("no public constructor", d[0].Message);
    }

    [Fact]
    public void Tier3d_EventArity()
    {
        var d = Analyze(AppDomainBinding + "AppDomain.currentDomain.addEventListener(\"ProcessExit\");");
        Assert.Single(d);
        Assert.Contains("requires (eventName, handler)", d[0].Message);
    }

    [Fact]
    public void Tier3d_UnknownEvent()
    {
        var d = Analyze(AppDomainBinding + "AppDomain.currentDomain.addEventListener(\"Typo\", (s: any, a: any) => {});");
        Assert.Single(d);
        Assert.Contains("Event 'Typo' not found", d[0].Message);
    }

    // Valid bindings: PascalCase mapping, constructor, fields, statics, and a valid event
    // subscription — must yield zero diagnostics on BOTH resolver paths.
    private const string ValidBindings =
        "@DotNetType(\"System.Text.StringBuilder\")\n" +
        "declare class StringBuilder {\n" +
        "  constructor();\n" +
        "  append(value: string): StringBuilder;\n" +
        "  toString(): string;\n" +
        "  readonly length: number;\n" +
        "}\n" + AppDomainBinding +
        "AppDomain.currentDomain.addEventListener(\"ProcessExit\", (s: any, a: any) => {});\n";

    [Fact]
    public void Clean_InProcessResolver_NoDiagnostics()
        => Assert.Empty(Analyze(ValidBindings));

    [Fact]
    public void Clean_LoaderResolver_NoDiagnostics()
    {
        using var loader = new AssemblyReferenceLoader(Array.Empty<string>());
        Assert.Empty(Analyze(ValidBindings, loader.TryResolve));
    }

    [Fact]
    public void PreciseColumns_PointAtTheMemberToken()
    {
        // 'appendx' starts at column 20 on line 2 (after "declare class SB { ").
        const string src = "@DotNetType(\"System.Text.StringBuilder\")\ndeclare class SB { appendx(v: string): SB; }";
        var tokens = new Lexer(src).ScanTokens();
        var parsed = new Parser(tokens, DecoratorMode.Stage3).Parse();
        var diags = new InteropAnalyzer().Analyze(parsed.Statements, new PositionMap(src));

        Assert.Single(diags);
        var loc = diags[0].Location;
        Assert.NotNull(loc);
        Assert.Equal(2, loc!.Line);
        Assert.Equal(20, loc.Column);     // start of 'appendx' (1-based)
        Assert.Equal(27, loc.EndColumn);  // end of the 7-char token
    }

    // --- dotnet: import specifiers (epic #1195) ---

    [Fact]
    public void DotNetImport_Valid_NoDiagnostics()
        => Assert.Empty(Analyze(
            "import { StringBuilder } from \"dotnet:System.Text.StringBuilder\";\n" +
            "import { Guid, Math as SysMath } from \"dotnet:System\";\n" +
            "const sb = new StringBuilder();"));

    [Fact]
    public void DotNetImport_UnresolvableName_Diagnostic()
    {
        var d = Analyze("import { Nope } from \"dotnet:System.NoSuchNs\";");
        Assert.Single(d);
        Assert.Contains("Nope", d[0].Message);
        Assert.Contains("System.NoSuchNs", d[0].Message);
    }

    [Fact]
    public void DotNetImport_DefaultAndStarForms_Diagnostics()
    {
        var d = Analyze(
            "import SB from \"dotnet:System.Text.StringBuilder\";\n" +
            "import * as Text from \"dotnet:System.Text\";");
        Assert.Equal(2, d.Count);
        Assert.Contains("named imports only", d[0].Message);
        Assert.Contains("namespace imports", d[1].Message);
    }

    [Fact]
    public void DotNetImport_BindingsFeedEventValidation()
    {
        // Imported types participate in Tier 3d exactly like @DotNetType bindings.
        var d = Analyze(
            "import { AppDomain } from \"dotnet:System\";\n" +
            "AppDomain.currentDomain.addEventListener(\"Typo\", (s: any, a: any) => {});");
        Assert.Single(d);
        Assert.Contains("Event 'Typo' not found", d[0].Message);
    }
}
